using System;
using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

/* ========================== */
/*   Floor Plan Interpreter   */
/* ========================== */
/*
 * Unity에서 평면도 분석에 필요한 처리를 담당
 */

public class FloorPlanInterpreter : MonoBehaviour
{
    public static FloorPlanInterpreter instance = null;

    private Process process = null;
    private SynchronizationContext unityCtx;


    //todo : 빌드 이후에 사용하려면 dataPath말고 streamingAssetsPath 로 써야함
    private string exePath => System.IO.Path.Combine(Application.dataPath, "07_Python", "Infer_WallMask.exe");

    public bool IsRunning => process != null && !process.HasExited;

    void Awake()
    {
        if (instance == null)
            instance = this;
        else if(instance != this)
            Destroy(gameObject);

        DontDestroyOnLoad(gameObject);

        
        unityCtx = SynchronizationContext.Current;
    }

    void OnApplicationQuit()
    {
        TerminateBackground();
    }

    /* ================================== */
    /*      백그라운드 프로세스 생성      */
    /* ================================== */
    void ExecuteBackground(string inputFilePath)
    {
        // process는 하나만 생성할 예정
        if (process != null)
        {
            UnityEngine.Debug.Log("Background process already running");
            return;
        }

        // 실행 파일에 전달할 인자
        string modelPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exePath), "wall_deeplabv3p_best.pth"));
        string outputPath = Path.Combine(Path.GetDirectoryName(inputFilePath), "Output_WallMask");
        var args = $"--ckpt \"{modelPath}\" --inputs \"{inputFilePath}\" --out_dir \"{outputPath}\" --size 1024 --th 0.5";

        UnityEngine.Debug.Log($"Running with args: {args}"); // 디버깅용 로그

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args, //필요시 인자
            WorkingDirectory = System.IO.Path.GetDirectoryName(exePath),
            UseShellExecute = false, // 꼭 false (리다이렉트/백그라운드)
            CreateNoWindow = true, // 콘솔 창 숨김
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, //로그 읽기 원하면
            RedirectStandardError = true,
            RedirectStandardInput = false //stdin 통신 원하면 true
        };

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"[PY] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError($"[PY ERR] {e.Data}");
        };

        if (process.Start())
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            UnityEngine.Debug.Log("Interpreter Started");
        }
        else
        {
            UnityEngine.Debug.LogError("Failed to start Interpreter");
        }
    }

    /* ================================== */
    /*   실행한 백그라운드 종료될 경우    */
    /* ================================== */
    private void OnInterpreterExited()
    {
        try
        {
            int code = process.ExitCode;
            Debug.Log($"Interpreter exited. code = {code}");

            // todo : 평면도 분석 완료 -> 다음 화면으로 넘어가기
        }
        catch(Exception ex)
        { 
            Debug.LogError(ex);
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }


    /* ================================== */
    /*  실행한 백그라운드 프로세스 종료   */
    /* ================================== */
    void TerminateBackground()
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // 
        }
        finally
        {
            process?.Dispose();
            process = null;
        }
    }


    /* ========================== */
    /*         평면도 입력        */
    /* ========================== */
    // 파일 탐색기 열어서 이미지 파일만 하나 선택
    // 선택된 이미지 파일은 imgPath 에 저장
    public void SelectFloorPlan()
    {
        // 파일 선택(이미지 필터)
        var exts = new[] { new SFB.ExtensionFilter("Image", "png", "jpg", "jpeg", "bmp", "tga", "gif") };
        var paths = SFB.StandaloneFileBrowser.OpenFilePanel("이미지 선택", "", exts, false);
        if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return;

        string src = paths[0];
        string ext = Path.GetExtension(src).ToLowerInvariant();

        // 저장(런타임은 persistentDataPath)
        string dir = Path.Combine(Application.persistentDataPath, "UserData", "FloorPlan");
        Directory.CreateDirectory(dir);
        string dst = Path.Combine(dir, "InputFloorPlan" + ext);
        File.Copy(src, dst, true);

        UnityEngine.Debug.Log($"런타임 복사 완료: {dst}");
    }

    /* ========================== */
    /*         평면도 분석        */
    /* ========================== */
    public void InterpretFloorPlan(string inputFilePath)
    {
        ExecuteBackground(inputFilePath);

        // Background 프로세스가 종료될 때까지 대기 (loading 창 보여주기)
    }
}