using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

//初步感觉，context的调用顺序决定了渲染顺序，context是一股脑submit的，如果想要立即执行一些gpucmd（不仅仅是渲染指令
//还可以是一些监视指令，状态改变指令等等）
public partial class SingleCameraRenderer
{
    //目前支持的
    static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
    static ShaderTagId litShaderTagId = new ShaderTagId("CustomLit");

    ScriptableRenderContext context;
    Camera camera;
    CullingResults cullingResults;
    Lighting lighting = new Lighting();

    //新建一块buffer给单个camerarendere使用
    const string bufferName = "Render Single Camera";
    CommandBuffer buffer = new CommandBuffer();

    CommandBuffer sample_Buffer = new CommandBuffer();

    //从RP进来的渲染流程
    public void Render(ScriptableRenderContext context, Camera camera, bool useDynamicBatching, bool useGPUInstancing)
    {
        this.context = context;
        this.camera = camera;
        PrepareBuffer();
        PrepareForSceneWindow();
        //先剔除
        if (!Cull())
        {
            return;
        }

        //再设置渲染初值
        Setup();
        lighting.Setup(context, cullingResults);
        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing);
        DrawUnsupportedShaders();
        DrawGizmos();
        Submit();
    }

    //一些初始化，比如vp矩阵,数据是从cam的transform结构中读取的
    //再次 view矩阵将坐标从世界转换到cam，project则将frustum转到box/ndc
    void Setup()
    {
        context.SetupCameraProperties(camera);
        CameraClearFlags flags = camera.clearFlags;//clear flag决定背景渲染1 to 4 they are Skybox, Color, Depth, and Nothing
        bool clearDepth = flags <= CameraClearFlags.Depth;
        bool clearColor = flags == CameraClearFlags.Color;
        buffer.ClearRenderTarget(clearDepth, clearColor, clearColor ?
                camera.backgroundColor.linear : Color.clear);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();

    }

    //初步剔除工作
    bool Cull()
    {
        //out关键字可以直接定义在参数列表中
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            cullingResults = context.Cull(ref p);//这一步是真正剔除，上面只是获取参数
            return true;
        }
        return false;
    }

    //先绘制一些几何
    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing)
    {
        //不透明物体绘制
        var sortingSettings = new SortingSettings(camera)
        {
            criteria = SortingCriteria.CommonOpaque
        };
        var drawingSettings = new DrawingSettings(
            unlitShaderTagId,
            sortingSettings
        )
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing
        };
        drawingSettings.SetShaderPassName(1, litShaderTagId);

        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        //绘制场景中的物体，这里会创建一个drawloop
        context.DrawRenderers(
            cullingResults, ref drawingSettings, ref filteringSettings
        );

        //天空盒       
        using (new FrameDebuggerSampler(this, "Draw Skybox"))
        {
            context.DrawSkybox(camera);
        }


        //透明物体绘制
        using (new FrameDebuggerSampler(this, "Draw Trans and UI"))
        {
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        }

    }

    //context真正执行
    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();
        context.Submit();
    }

    //context中的cmdBuffer真正执行
    public void ExecuteBuffer(CommandBuffer buffer = null)
    {
        if (buffer == null)
            buffer = this.buffer;

        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    public void SetSampleBufferName(string name)
    {
        sample_Buffer.name = name;
    }

    public CommandBuffer GetSampleBuffer()
    {
        return this.sample_Buffer;
    }
}
public class FrameDebuggerSampler : IDisposable
{
    SingleCameraRenderer renderer;
    string sample_name;
    CommandBuffer sample_buffer;

    public FrameDebuggerSampler(SingleCameraRenderer renderer, string sampleName)
    {
        this.renderer = renderer;
        this.sample_name = sampleName;
        sample_buffer = renderer.GetSampleBuffer();
        renderer.SetSampleBufferName(sample_name);

        sample_buffer.BeginSample(sampleName);
        renderer.ExecuteBuffer(sample_buffer);
    }

    public void Dispose()
    {
        sample_buffer.EndSample(sample_name);
        renderer.ExecuteBuffer(sample_buffer);
    }
}
