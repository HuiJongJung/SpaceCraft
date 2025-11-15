Shader "VertexStudio/Built-In/Metallic Color Mask" {
    Properties {	
 
        [Header(Masked colors)]   
        [Space(5)]      
        [NoScaleOffset][SingleLineTexture]_Mask ("Colors Mask (RGB)", 2D) = "black" {}
        _ColorR ("Color (R)", Color) = (1,1,1,1)
        _ColorG ("Color (G)", Color) = (1,1,1,1)
        _ColorB ("Color (B)", Color) = (1,1,1,1)        

        [Header(Surface variables)]
        [Space(5)] 
 		[NoScaleOffset][SingleLineTexture]_MainTex ("Albedo", 2D) = "white" {}      

        [Space(10)]  
        [NoScaleOffset][SingleLineTexture]_BumpMap ("Normal Map", 2D) = "bump" {}

        [Space(10)]  
        [NoScaleOffset][SingleLineTexture]_MetallicGlossMap ("Metallic(R), AO(G), Smoothness(A)", 2D) = "white" {}        
        _Metallic ("Metallic", Range(0.0, 1.0)) = 1.0
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 1.0       
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0

        [Space(10)]        
        [NoScaleOffset][SingleLineTexture]_EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR]_EmissionColor("Emission Color", Color) = (0,0,0)

        [Header(UV variables)]
        [Space(5)] 
        _ScaleAndOffset ("Scale (XY), Offset(XZ)", Vector) = (1.0, 1.0, 0.0, 0.0)

    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
       
        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows
 
        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0
 
        sampler2D   _MainTex,_Mask;
        sampler2D   _BumpMap;
        sampler2D   _MetallicGlossMap;
        sampler2D   _EmissionMap;
        half        _OcclusionStrength, _Glossiness, _Metallic;
        half4       _EmissionColor;
        fixed4 _ColorR,_ColorG,_ColorB;
        half4 _ScaleAndOffset;

        struct Input {
            float2 uv_MainTex;
        };
 
        void surf (Input IN, inout SurfaceOutputStandard o) {

            float2 uv = IN.uv_MainTex * _ScaleAndOffset.xy + _ScaleAndOffset.zw;
 
            fixed4 mask = tex2D (_Mask, uv);
            fixed noMask = 1 - max(max(mask.r, mask.g), mask.b);
            fixed4 albedoTex = tex2D (_MainTex, uv) * saturate( ( _ColorR * mask.r + _ColorG * mask.g + _ColorB * mask.b + noMask) );
 
            o.Albedo = albedoTex.rgb;
 
            o.Normal = UnpackNormal ( tex2D (_BumpMap, uv) );
 
            fixed4 metallic = tex2D (_MetallicGlossMap, uv);
 
            o.Metallic = metallic.r * _Metallic;
 
            o.Smoothness = metallic.a * _Glossiness;

            o.Occlusion = LerpOneTo (metallic.g, _OcclusionStrength);
 
            o.Emission = _EmissionColor * ( tex2D (_EmissionMap, uv) );
 
            o.Alpha = albedoTex.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}