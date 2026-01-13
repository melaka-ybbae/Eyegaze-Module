"""
L2CS-Net PyTorch 모델을 ONNX로 변환하는 스크립트

사용법:
1. 먼저 L2CS-Net 가중치 다운로드:
   - Google Drive: https://drive.google.com/drive/folders/1qDzyzXO6iaYe4W1MK0xeqoP7FE6MxVYb
   - L2CSNet_gaze360.pkl 파일 다운로드

2. 필요한 패키지 설치:
   pip install torch torchvision onnx onnxruntime

3. 스크립트 실행:
   python convert_l2cs_to_onnx.py --weights L2CSNet_gaze360.pkl --output l2cs_gaze360.onnx
"""

import sys
import os

# L2CS-Net 레포 경로 추가
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'L2CS-Net-repo'))

import argparse
import torch
import torch.nn as nn
import torchvision


# L2CS 모델 정의 (원본 l2cs/model.py에서 가져옴)
class L2CS(nn.Module):
    def __init__(self, block, layers, num_bins):
        import math
        self.inplanes = 64
        super(L2CS, self).__init__()
        self.conv1 = nn.Conv2d(3, 64, kernel_size=7, stride=2, padding=3, bias=False)
        self.bn1 = nn.BatchNorm2d(64)
        self.relu = nn.ReLU(inplace=True)
        self.maxpool = nn.MaxPool2d(kernel_size=3, stride=2, padding=1)
        self.layer1 = self._make_layer(block, 64, layers[0])
        self.layer2 = self._make_layer(block, 128, layers[1], stride=2)
        self.layer3 = self._make_layer(block, 256, layers[2], stride=2)
        self.layer4 = self._make_layer(block, 512, layers[3], stride=2)
        self.avgpool = nn.AdaptiveAvgPool2d((1, 1))

        self.fc_yaw_gaze = nn.Linear(512 * block.expansion, num_bins)
        self.fc_pitch_gaze = nn.Linear(512 * block.expansion, num_bins)

        # Vestigial layer from previous experiments
        self.fc_finetune = nn.Linear(512 * block.expansion + 3, 3)

        for m in self.modules():
            if isinstance(m, nn.Conv2d):
                n = m.kernel_size[0] * m.kernel_size[1] * m.out_channels
                m.weight.data.normal_(0, math.sqrt(2. / n))
            elif isinstance(m, nn.BatchNorm2d):
                m.weight.data.fill_(1)
                m.bias.data.zero_()

    def _make_layer(self, block, planes, blocks, stride=1):
        downsample = None
        if stride != 1 or self.inplanes != planes * block.expansion:
            downsample = nn.Sequential(
                nn.Conv2d(self.inplanes, planes * block.expansion,
                          kernel_size=1, stride=stride, bias=False),
                nn.BatchNorm2d(planes * block.expansion),
            )

        layers = []
        layers.append(block(self.inplanes, planes, stride, downsample))
        self.inplanes = planes * block.expansion
        for i in range(1, blocks):
            layers.append(block(self.inplanes, planes))

        return nn.Sequential(*layers)

    def forward(self, x):
        x = self.conv1(x)
        x = self.bn1(x)
        x = self.relu(x)
        x = self.maxpool(x)

        x = self.layer1(x)
        x = self.layer2(x)
        x = self.layer3(x)
        x = self.layer4(x)
        x = self.avgpool(x)
        x = x.view(x.size(0), -1)

        # gaze
        pre_yaw_gaze = self.fc_yaw_gaze(x)
        pre_pitch_gaze = self.fc_pitch_gaze(x)
        return pre_yaw_gaze, pre_pitch_gaze


def getArch(arch, bins):
    """모델 아키텍처 선택"""
    if arch == 'ResNet18':
        model = L2CS(torchvision.models.resnet.BasicBlock, [2, 2, 2, 2], bins)
    elif arch == 'ResNet34':
        model = L2CS(torchvision.models.resnet.BasicBlock, [3, 4, 6, 3], bins)
    elif arch == 'ResNet101':
        model = L2CS(torchvision.models.resnet.Bottleneck, [3, 4, 23, 3], bins)
    elif arch == 'ResNet152':
        model = L2CS(torchvision.models.resnet.Bottleneck, [3, 8, 36, 3], bins)
    else:
        # 기본값: ResNet50
        model = L2CS(torchvision.models.resnet.Bottleneck, [3, 4, 6, 3], bins)
    return model


def convert_to_onnx(weights_path: str, output_path: str, arch: str = 'ResNet50', num_bins: int = 90):
    """
    PyTorch 모델을 ONNX로 변환
    """
    print(f"Loading weights from: {weights_path}")
    print(f"Architecture: {arch}")

    # 모델 생성
    model = getArch(arch, num_bins)

    # 가중치 로드
    state_dict = torch.load(weights_path, map_location='cpu')

    # state_dict 키 확인 및 조정
    if 'model_state_dict' in state_dict:
        state_dict = state_dict['model_state_dict']

    # 키 이름 매핑 (필요시)
    new_state_dict = {}
    for key, value in state_dict.items():
        # 'module.' prefix 제거 (DataParallel로 학습된 경우)
        if key.startswith('module.'):
            key = key[7:]
        new_state_dict[key] = value

    # 가중치 로드
    missing_keys, unexpected_keys = model.load_state_dict(new_state_dict, strict=False)

    if missing_keys:
        print(f"Warning: Missing keys: {missing_keys}")
    if unexpected_keys:
        print(f"Warning: Unexpected keys: {unexpected_keys}")

    model.eval()
    print("Model loaded successfully")

    # 더미 입력 생성 (L2CS-Net은 448x448 입력)
    dummy_input = torch.randn(1, 3, 448, 448)

    print(f"Exporting to ONNX: {output_path}")

    # ONNX 내보내기
    torch.onnx.export(
        model,
        dummy_input,
        output_path,
        export_params=True,
        opset_version=12,
        do_constant_folding=True,
        input_names=['input'],
        output_names=['yaw', 'pitch'],  # L2CS-Net은 yaw를 먼저 출력
        dynamic_axes={
            'input': {0: 'batch_size'},
            'yaw': {0: 'batch_size'},
            'pitch': {0: 'batch_size'}
        }
    )

    print(f"ONNX model saved to: {output_path}")

    # 검증
    try:
        import onnx
        onnx_model = onnx.load(output_path)
        onnx.checker.check_model(onnx_model)
        print("ONNX model validation passed!")

        # 입출력 정보 출력
        print("\nModel inputs:")
        for inp in onnx_model.graph.input:
            shape = [d.dim_value for d in inp.type.tensor_type.shape.dim]
            print(f"  - {inp.name}: {shape}")

        print("\nModel outputs:")
        for output in onnx_model.graph.output:
            shape = [d.dim_value for d in output.type.tensor_type.shape.dim]
            print(f"  - {output.name}: {shape}")

    except ImportError:
        print("onnx package not installed, skipping validation")

    # ONNX Runtime으로 테스트
    try:
        import onnxruntime as ort
        import numpy as np

        session = ort.InferenceSession(output_path)
        test_input = np.random.randn(1, 3, 448, 448).astype(np.float32)
        outputs = session.run(None, {'input': test_input})

        print(f"\nTest inference successful!")
        print(f"  Yaw output shape: {outputs[0].shape}")
        print(f"  Pitch output shape: {outputs[1].shape}")

    except ImportError:
        print("onnxruntime package not installed, skipping inference test")


def download_weights():
    """
    가중치 다운로드 안내
    """
    print("""
=== L2CS-Net 가중치 다운로드 방법 ===

1. 브라우저에서 Google Drive 링크 열기:
   https://drive.google.com/drive/folders/1qDzyzXO6iaYe4W1MK0xeqoP7FE6MxVYb

2. 다음 파일 중 하나를 다운로드:
   - L2CSNet_gaze360.pkl (Gaze360 데이터셋으로 학습, 범용)
   - L2CSNet_mpiigaze.pkl (MPIIGaze 데이터셋으로 학습, 실내용)

3. 다운로드 후 이 스크립트와 같은 폴더에 저장

4. 변환 명령어:
   python convert_l2cs_to_onnx.py --weights L2CSNet_gaze360.pkl --output Assets/Models/l2cs_gaze360.onnx

추천: L2CSNet_gaze360.pkl
- Gaze360 정확도: 10.41°
- MPIIGaze 정확도: 3.92° (ResNet50 기준)
- 더 넓은 시선 범위 지원 (-180° ~ +180°)
""")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Convert L2CS-Net PyTorch model to ONNX')
    parser.add_argument('--weights', type=str, help='Path to PyTorch weights (.pkl)')
    parser.add_argument('--output', type=str, default='l2cs_net.onnx', help='Output ONNX path')
    parser.add_argument('--arch', type=str, default='ResNet50',
                        choices=['ResNet18', 'ResNet34', 'ResNet50', 'ResNet101', 'ResNet152'],
                        help='Model architecture (default: ResNet50)')
    parser.add_argument('--num_bins', type=int, default=90, help='Number of bins (default: 90)')
    parser.add_argument('--download', action='store_true', help='Show download instructions')

    args = parser.parse_args()

    if args.download or args.weights is None:
        download_weights()
    else:
        convert_to_onnx(args.weights, args.output, args.arch, args.num_bins)
