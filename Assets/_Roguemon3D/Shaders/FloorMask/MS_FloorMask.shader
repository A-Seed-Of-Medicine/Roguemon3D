Shader "Custom/FloorMask"
{
    Properties
    {
        [IntRange] _StencilID("Stencil ID", Range(0, 255)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue"="Geometry-1" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend Zero One
            Zwrite Off
            Stencil
            {
                Ref [_StencilID]
                Comp Always
                Pass Replace
            }
        }
    }
}
