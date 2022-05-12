using UnityEngine;
using UnityEngine.Rendering;

public class PostFXStack
{
    //后处理用到的pass
    enum Pass
    {
        Copy
    }

    const string bufferName = "Post FX";
    CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    ScriptableRenderContext context;
    Camera camera;
    PostFXSettings settings;

    public bool IsActive => settings != null;

    int fxSourceId = Shader.PropertyToID("_PostFXSource");

    public void Setup(
        ScriptableRenderContext context, Camera camera, PostFXSettings settings
    )
    {
        this.context = context;
        this.camera = camera;
        this.settings = camera.cameraType <= CameraType.SceneView ? settings : null;
    }

    public void Render(int sourceId)
    {
        //将source复制到输出的rt
        //buffer.Blit(sourceId, BuiltinRenderTextureType.CameraTarget);
        Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass)
    {
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(
            to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
        );
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass,
            MeshTopology.Triangles, 3
        );
    }
}