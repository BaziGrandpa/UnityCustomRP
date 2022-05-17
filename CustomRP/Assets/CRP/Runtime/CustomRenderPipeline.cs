using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    bool useDynamicBatching, useGPUInstancing;
    ShadowSettings shadowSettings;
    PostFXSettings postFXSettings;
    CameraBufferSettings cameraBufferSettings;

    public CustomRenderPipeline(CameraBufferSettings cameraBufferSettings, bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher, ShadowSettings shadowSettings, PostFXSettings postFXSettings, Shader cameraRendererShader)
    {
        this.cameraBufferSettings = cameraBufferSettings;
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        this.shadowSettings = shadowSettings;
        this.postFXSettings = postFXSettings;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        cam_renderer = new SingleCameraRenderer(cameraRendererShader);
    }

    SingleCameraRenderer cam_renderer;
    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var cam in cameras)
        {
            cam_renderer.Render(context, cam, cameraBufferSettings, useDynamicBatching, useGPUInstancing, shadowSettings, postFXSettings);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        //DisposeForEditor();
        cam_renderer.Dispose();
    }
}
