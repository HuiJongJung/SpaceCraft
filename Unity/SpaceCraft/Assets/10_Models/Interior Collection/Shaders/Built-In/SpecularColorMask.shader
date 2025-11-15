Shader "VertexStudio/Built-In/Specular Color Mask" {
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
        [NoScaleOffset][SingleLineTexture]_SpecGlossMap ("Specular (RGB), Smoothness(A)", 2D) = "white" {}
        _SpecColor("Specular Color", Color) = (0.2,0.2,0.2)
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 1.0

        [Space(10)]
        [NoScaleOffset][SingleLineTexture]_OcclusionMap("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0

        [Space(10)]        
        [NoScaleOffset][SingleLineTexture] _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR]_EmissionColor("Emission Color", Color) = (0,0,0)

        [Header(UV)]
        [Space(5)] 
        _ScaleAndOffset ("Scale (XY), Offset(XZ)", Vector) = (1.0, 1.0, 0.0, 0.0)

    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
       
        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf StandardSpecular fullforwardshadows
 
        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0
 
        sampler2D _MainTex,_Mask;
        sampler2D _BumpMap;
        sampler2D _SpecGlossMap;
        sampler2D _EmissionMap;
        sampler2D _OcclusionMap;
        half _Glossiness, _OcclusionStrength;
        half4 _EmissionColor;
        half4 _ScaleAndOffset;
 
        struct Input {
            float2 uv_MainTex;
        };
 
        fixed4 _ColorR,_ColorG,_ColorB;
 
        void surf (Input IN, inout SurfaceOutputStandardSpecular o) {

            float2 uv = IN.uv_MainTex * _ScaleAndOffset.xy + _ScaleAndOffset.zw;
 
            fixed4 mask = tex2D (_Mask, uv);
            fixed noMask = 1 - max(max(mask.r, mask.g), mask.b);
            fixed4 c = tex2D (_MainTex, uv) * saturate( ( _ColorR * mask.r + _ColorG * mask.g + _ColorB * mask.b + noMask) );
 
            half occ = tex2D(_OcclusionMap, uv).g;
            o.Occlusion = LerpOneTo (occ, _OcclusionStrength);
 
            o.Albedo = c.rgb;
 
            o.Normal = UnpackNormal ( tex2D (_BumpMap, uv) );
 
            fixed4 spec = tex2D (_SpecGlossMap, uv);
 
            o.Specular = spec.rgb * _SpecColor.rgb;
 
            o.Smoothness = spec.a * _Glossiness;
 
            o.Emission = _EmissionColor * ( tex2D (_EmissionMap, uv) );
 
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}