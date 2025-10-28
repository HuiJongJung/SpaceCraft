using UnityEngine;
using System.Diagnostics;
using System.IO;

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

    //todo : 빌드 이후에 사용하려면 dataPath말고 streamingAssetsPath 로 써야함
    private string exePath => System.IO.Path.Combine(Application.dataPath, "Python", "OpenCV_WallExtractor.exe");
    private string imgPath; // 현재 프로젝트의 Assets/UserData/FloorPlan/InputFloorPlan.이미지확장자 에 저장 예정

    //private Sprite previewImage = null;

    void Awake()
    {
        if (instance == null)
            instance = this;

        else if(instance != this)
            Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    void OnApplicationQuit()
    {
        TerminateBackground();
    }

    /* ================================== */
    /*      백그라운드 프로세스 생성      */
    /* ================================== */
    void ExecuteBackground()
    {
        // process는 하나만 생성할 예정
        if (process != null)
        {
            UnityEngine.Debug.Log("Background process already running");
            return;
        }

        // 실행 파일에 전달할 인자
        var args = $"";

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
    /*  실행한 백그라운드 프로세스 종료   */
    /* ================================== */
    void TerminateBackground()
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill();
                process.Dispose();
            }
        }
        catch
        {
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
    public void InterpretFloorPlan()
    {
        ExecuteBackground();

        // Background 프로세스가 종료될 때까지 대기 (loading 창 보여주기)
    }
}