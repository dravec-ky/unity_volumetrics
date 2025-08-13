using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

namespace Playtonic.Game
{
    public enum BufferType
    {
        CameraColor,
        Custom 
    }

    // --------------------------------------------------------------------

    public class DrawVolumetricsFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            public RenderTexture renderTarget;
            [Range(0.1f, 2.0f)]
            public float renderScale = 1.0f;

            public Material blitMaterial = null;
            public Texture3D cloudNoiseTexture;
            public int blitMaterialPassIndex = -1;
            public BufferType sourceType = BufferType.CameraColor;
            public BufferType destinationType = BufferType.CameraColor;
            public string sourceTextureId = "_SourceTexture";
            public string destinationTextureId = "_DestinationTexture";
            public bool safePassFunction = true;
        }

        public string[] PassNames;

        public Settings settings = new Settings();
        public DrawVolumetricsPass blitPass;
        public LayerMask layerMask;

    // --------------------------------------------------------------------

        public override void Create()
        {
            blitPass = new DrawVolumetricsPass(name, PassNames, layerMask);
        }

    // --------------------------------------------------------------------

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            blitPass.renderPassEvent = settings.renderPassEvent;
            blitPass.settings = settings;
            renderer.EnqueuePass(blitPass);
        }
    }
}