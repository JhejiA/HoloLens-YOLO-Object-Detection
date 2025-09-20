using System;
using UnityEngine;

public enum AppState
{
    IDLE,
    LISTENING,
    PROCESSING_SPEECH,
    SEARCHING,
    FOUND,
    ERROR
}

public class ApplicationFSM : MonoBehaviour
{
    public AppState CurrentState { get; private set; } = AppState.IDLE;
    public static event Action<AppState, AppState> OnStateChanged;
    
    private YoloDetector yoloDetector;
    private float stateTimer = 0f;
    private float searchTimeout = 30f;
    
    void Awake()
    {
        yoloDetector = GetComponent<YoloDetector>();
    }
    
    void Update()
    {
        stateTimer += Time.deltaTime;
        
        // 超时处理
        if (CurrentState == AppState.SEARCHING && stateTimer > searchTimeout)
        {
            Debug.Log("搜索超时，返回IDLE");
            TransitionTo(AppState.IDLE);
        }
    }
    
    public void TransitionTo(AppState newState)
    {
        if (CurrentState == newState) return;
        
        var oldState = CurrentState;
        ExitState(CurrentState);
        CurrentState = newState;
        EnterState(newState);
        stateTimer = 0f;
        
        OnStateChanged?.Invoke(oldState, newState);
        Debug.Log($"[FSM] {oldState} -> {newState}");
    }
    
    private void EnterState(AppState state)
    {
        switch (state)
        {
            case AppState.IDLE:
                // IDLE时关闭检测，节省性能
                if (yoloDetector != null)
                    yoloDetector.SetDetectionEnabled(false);
                break;
                
            case AppState.LISTENING:
                // 开始录音
                break;
                
            case AppState.PROCESSING_SPEECH:
                // 显示处理中UI
                break;
                
            case AppState.SEARCHING:
                // 只在搜索时开启检测
                if (yoloDetector != null)
                {
                    yoloDetector.SetDetectionEnabled(true);
                    yoloDetector.SetTargetProduct(GetCurrentSearchTarget());
                }
                break;
                
            case AppState.FOUND:
                // 停止检测，显示结果
                if (yoloDetector != null)
                    yoloDetector.SetDetectionEnabled(false);
                break;
                
            case AppState.ERROR:
                // 错误处理
                if (yoloDetector != null)
                    yoloDetector.SetDetectionEnabled(false);
                Invoke(nameof(RecoverFromError), 3f);
                break;
        }
    }
    
    private void ExitState(AppState state)
    {
        // 清理当前状态
    }
    
    private string GetCurrentSearchTarget()
    {
        // 从语音识别结果获取
        return MainController.Instance?.CurrentSearchTarget ?? "";
    }
    
    private void RecoverFromError()
    {
        TransitionTo(AppState.IDLE);
    }
}