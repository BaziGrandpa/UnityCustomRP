using UnityEditor;
using UnityEngine;

partial class PostFXStack
{

    partial void ApplySceneViewState();

#if UNITY_EDITOR

    partial void ApplySceneViewState()
    {
        //场景相机不用后处理
        if (camera.cameraType == CameraType.SceneView 
   // !SceneView.currentDrawingSceneView.sceneViewState.showImageEffects
        )
        {
            settings = null;
        }
    }

#endif
}