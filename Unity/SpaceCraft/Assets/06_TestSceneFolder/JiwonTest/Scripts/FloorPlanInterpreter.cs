using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

/* ========================== */
/*   Floor Plan Interpreter   */
/* ========================== */

public class FloorPlanInterpreter : MonoBehaviour
{
    public FloorPlanUI floorPlanUI;

    private Process inferProcess = null;
    private Process convProcess = null;

    // Program Path
    private string inferencePath;
    private string convertPath;

    // Output Path
    private string outMaskPath;
    private string outJsonPath;

    public bool isProcessing = false;
    private bool isFinished = false;


    void Awake()
    {
        inferencePath = Path.Combine(Application.streamingAssetsPath, "FloorPlan", "Inference_FloorPlan", "Inference_FloorPlan.exe");
        convertPath = Path.Combine(Application.streamingAssetsPath, "FloorPlan", "Convert_FloorPlan", "Convert_FloorPlan.exe");

        outMaskPath = Path.Combine(Application.persistentDataPath, "UserData", "floorplan_mask.png");
        outJsonPath = Path.Combine(Application.persistentDataPath, "UserData", "space.json");
    }

    void OnApplicationQuit()
    {
        TerminateBackground();
    }

    void Update()
    {
        if (isFinished)
        {
            floorPlanUI.ApplyOutput(true);
            isFinished = false;
        }
    }


    public void InterpretFloorPlan(string inputFilePath)
    {
        // 입력된 이미지로 평면도 분석

        // 한 번에 하나의 프로세스만 실행
        if (isProcessing)
        {
            UnityEngine.Debug.LogWarning("[Interpreter] 프로세스가 이미 실행 중입니다.");
            floorPlanUI.ApplyOutput(false);
            return;
        }

        // 입력 파일 검사
        if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
        {
            UnityEngine.Debug.LogError($"[Interpreter] 입력 파일이 유효하지 않습니다 : {inputFilePath}");
            floorPlanUI.ApplyOutput(false);
            return;
        }

        // 프로세스 시작
        isProcessing = true;
        UnityEngine.Debug.Log("[Interpreter] 평면도 분석 시작 (Step : Inference)");

        ExecuteInferenceProcess(inputFilePath);
    }


    /* ================================== */
    /*      백그라운드 프로세스 로직      */
    /* ================================== */

    #region Inference
    void ExecuteInferenceProcess(string inputFilePath)
    {
        // 기존 프로세스 정리
        if (inferProcess != null)
        {
            if(!inferProcess.HasExited)
                inferProcess.Kill();
            
            inferProcess.Dispose();
        }
        
        // Process 생성
        var args = $"--input \"{inputFilePath}\" --output \"{outMaskPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = inferencePath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(inferencePath),
            UseShellExecute = false,    // 백그라운드 실행(false)
            CreateNoWindow = true,      // 콘솔 창 숨김(true)
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, // Process 로그 읽기
            RedirectStandardError = true,
            RedirectStandardInput = false  // stdin 통신
        };

        inferProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // 로그 연결
        inferProcess.OutputDataReceived += (s, e) 
            => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"[Inference] {e.Data}"); };
        inferProcess.ErrorDataReceived += (s, e) 
            => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError($"[Inference Error] {e.Data}"); };

        // 프로세스 종료까지 대기
        inferProcess.Exited += OnInferenceExited;

        try
        {
            inferProcess.Start();
            inferProcess.BeginOutputReadLine();
            inferProcess.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[Interpreter] Inference 실행 실패: {e.Message}");
            ResetState(); // 에러 발생 시 상태 초기화
        }
    }

    private void OnInferenceExited(object sender, EventArgs e)
    {
        // 별도의 스레드에서 실행
        
        int exitCode = inferProcess.ExitCode;
        UnityEngine.Debug.Log($"[Interpreter] Inference 종료 (ExitCode: {exitCode})");

        if (exitCode == 0)
        {
            // 성공 시 다음 단계 실행
            UnityEngine.Debug.Log("[Interpreter] 마스크 생성 완료. (Step 2: Convert)");
            ExecuteConvertProcess();
        }
        else
        {
            UnityEngine.Debug.LogError("[Interpreter] Inference 과정에서 오류가 발생하여 중단합니다.");
            ResetState();
        }
    }
    #endregion

    #region Convert
    private void ExecuteConvertProcess()
    {
        // 기존 프로세스 정리
        if (convProcess != null && !convProcess.HasExited) convProcess.Kill();
        convProcess?.Dispose();

        // Inference에서 생성된 outMaskPath를 입력으로 사용
        var args = $"--input \"{outMaskPath}\" --output \"{outJsonPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = convertPath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(convertPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };

        convProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        convProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"[Convert] {e.Data}"); };
        convProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError($"[Convert Error] {e.Data}"); };

        // [중요] Convert 종료 이벤트 연결
        convProcess.Exited += OnConvertExited;

        try
        {
            convProcess.Start();
            convProcess.BeginOutputReadLine();
            convProcess.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Convert 실행 실패: {e.Message}");
            ResetState();
        }
    }

    // Convert 종료 콜백
    private void OnConvertExited(object sender, EventArgs e)
    {
        int exitCode = convProcess.ExitCode;
        UnityEngine.Debug.Log($"Convert 종료 (ExitCode: {exitCode})");

        if (exitCode == 0)
        {
            // 프로세스 실행 완료 후 로직
        }
        else
        {
            UnityEngine.Debug.LogError("Convert 과정에서 오류가 발생했습니다.");
        }

        // 작업 완료 -> 상태 초기화 (다시 실행 가능하도록)
        ResetState();
    }

    #endregion


    /* ================================== */
    /*           유틸리티 / 종료          */
    /* ================================== */
    // 상태 초기화 함수
    private void ResetState()
    {
        isFinished = true;
        isProcessing = false;
        // 필요하다면 프로세스 핸들 정리
        // Dispose는 다음 실행 시 또는 종료 시 처리됨
    }

    void TerminateBackground()
    {
        try
        {
            if (inferProcess != null && !inferProcess.HasExited) inferProcess.Kill();
        }
        catch { /* Ignore */ }
        finally { inferProcess?.Dispose(); inferProcess = null; }

        try
        {
            if (convProcess != null && !convProcess.HasExited) convProcess.Kill();
        }
        catch { /* Ignore */ }
        finally { convProcess?.Dispose(); convProcess = null; }

        isProcessing = false;
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

}