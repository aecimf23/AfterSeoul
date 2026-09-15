using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace AfterSeoul.Unity.Editor
{
    public static class AlphaBuild
    {
        [MenuItem("After Seoul/Build Android Alpha")]
        public static void Android()
        {
            Directory.CreateDirectory("Builds/Android");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Builds/Android/AfterSeoul-alpha.apk",
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            var summary = report.summary;
            File.WriteAllText("Builds/Android/build-result.txt",
                $"{summary.result}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nBytes: {summary.totalSize}\nTime: {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Android build failed: " + summary.result);
        }
    }
}
