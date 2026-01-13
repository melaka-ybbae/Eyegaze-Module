using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildScript
{
    [MenuItem("Build/Build Android APK")]
    public static void BuildAndroidAPK()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();

        // 씬 목록
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/GazeDebugScene.unity" };

        // 출력 경로
        buildPlayerOptions.locationPathName = "Output/android/test.apk";

        // Android 타겟
        buildPlayerOptions.target = BuildTarget.Android;

        // Development Build 활성화 (로그 확인용)
        buildPlayerOptions.options = BuildOptions.Development | BuildOptions.AllowDebugging;

        // 빌드 실행
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded: {summary.totalSize} bytes");
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError("Build failed!");
        }
    }

    // 명령줄에서 호출용
    public static void BuildFromCommandLine()
    {
        BuildAndroidAPK();
    }
}
