using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键构建：注册标签、切换旧输入系统、生成场景、打包 Windows 可执行文件。
/// 供命令行批处理调用：
///   Tuanjie.exe -batchmode -nographics -quit -projectPath &lt;项目&gt;
///               -executeMethod BuildScript.BuildGame -logFile &lt;日志&gt;
/// </summary>
public static class BuildScript
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("星火/构建 Windows 版本")]
    public static void BuildGame()
    {
        try
        {
            // 1) 运行时代码用到的自定义标签
            EnsureTag("Enemy");
            EnsureTag("Ally");
            EnsureTag("Blizzard");
            EnsureTag("Campfire");

            // 2) 本项目使用旧版 Input Manager（Input.GetAxis / GetButtonDown）
            EnsureLegacyInput();

            // 3) 运行时 Shader.Find 依赖的着色器必须打进包体
            EnsureAlwaysIncludedShader("Standard");
            EnsureAlwaysIncludedShader("UI/Default");
            EnsureAlwaysIncludedShader("Sprites/Default");
            EnsureAlwaysIncludedShader("Skybox/Procedural");
            EnsureAlwaysIncludedShader("Universal Render Pipeline/Lit");

            // 4) 产品信息与窗口
            PlayerSettings.companyName = "StudyGame";
            PlayerSettings.productName = "Starfire";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            // 5) 创建唯一场景，挂上总控脚本
            Directory.CreateDirectory("Assets/Scenes");

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject bootstrap = new GameObject("GameBootstrap");
            bootstrap.AddComponent<GameBootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            // 6) 打包
            string outputDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../Build"));
            Directory.CreateDirectory(outputDir);

            string exePath = Path.Combine(outputDir, "Starfire.exe");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("BUILD_RESULT:Success " + exePath
                    + " size=" + report.summary.totalSize
                    + " time=" + report.summary.totalTime);
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("BUILD_RESULT:Failed " + report.summary.result);
                EditorApplication.Exit(1);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("BUILD_RESULT:Exception " + e);
            EditorApplication.Exit(2);
        }
    }

    // ───────────────────────────── 设置项 ─────────────────────────────

    private static void EnsureTag(string tag)
    {
        Object tagManager = AssetDatabase.LoadAllAssetsAtPath(
            "ProjectSettings/TagManager.asset")[0];

        SerializedObject so = new SerializedObject(tagManager);
        SerializedProperty tags = so.FindProperty("tags");
        if (tags == null) return;

        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log("BuildScript: registered tag " + tag);
    }

    private static void EnsureLegacyInput()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(
            "ProjectSettings/ProjectSettings.asset");
        if (assets.Length == 0) return;

        SerializedObject so = new SerializedObject(assets[0]);
        SerializedProperty handler = so.FindProperty("activeInputHandler");

        if (handler != null && handler.intValue != 0)
        {
            handler.intValue = 0; // 0 = Input Manager (Old)
            so.ApplyModifiedProperties();
            Debug.Log("BuildScript: activeInputHandler -> 0 (legacy)");
        }
    }

    private static void EnsureAlwaysIncludedShader(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null) return;

        Object settings = AssetDatabase.LoadAllAssetsAtPath(
            "ProjectSettings/GraphicsSettings.asset")[0];

        SerializedObject so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return;

        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
        }

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        so.ApplyModifiedProperties();

        Debug.Log("BuildScript: always include shader " + shaderName);
    }
}
