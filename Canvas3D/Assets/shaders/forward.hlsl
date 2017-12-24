﻿#include "helpers/lighting.hlsl"
#include "helpers/pbr.hlsl"

#define PBR_ENABLED (1)

struct PSInput {
   float3 positionObject : POSITION1;
   float3 positionWorld : POSITION2;
   float4 position : SV_Position;
   float3 normalObject : NORMAL1;
   float3 normalWorld : NORMAL2;
   float4 normal : NORMAL3;
   float4 color : COLOR;
   float2 uv : TEXCOORD;
};

PSInput VSMain(
   float3 position : POSITION, 
   float3 normal : NORMAL, 
   float4 color : COLOR, 
   float2 uv : TEXCOORD,
   float4x4 world : INSTANCE_TRANSFORM
) {
   PSInput result;

   result.positionObject = position;
   result.positionWorld = mul(world, float4(position, 1)).xyz;
   result.position = mul(mul(projView, world), float4(position, 1));
   result.normalObject = normal;
   result.normalWorld = mul(world, float4(normal, 0)).xyz; // must normalize in PS
   result.normal = normalize(mul(mul(projView, world), float4(normal, 0)));
   result.color = color;
   result.uv = uv;

   return result;
}

float4 PSMain(PSInput input) : SV_TARGET {
   uint numStructs, structStride;
   SpotlightDescriptions.GetDimensions(numStructs, structStride);
   
   // Extract input vert info
   float3 P = input.positionWorld;
   float3 N = normalize(input.normalWorld);
   float3 V = normalize(cameraEye - P);

   // Sample input raw material color (albedo)
   float4 baseAndTransparency = input.color * SampleDiffuseMap(input.uv, input.positionObject.xyz, input.normalObject.xyz);
   float3 base = baseAndTransparency.xyz;
   float transparency = baseAndTransparency.w;

   float3 colorAccumulator = 0;

   [branch] if (PBR_ENABLED) {
      float metallic, roughness;
      pbrMaterialProperties(input.positionWorld, metallic, roughness);

      float3 diffuse, F0;
      pbrMaterialDiffuseF0(base, metallic, diffuse, F0);

      // metallic doesn't diffuse.

      /**/
      float nDotV = dot(N, V);

      for (uint i = 0; i != numSpotlights; i++) {
         colorAccumulator += pbrComputeSpotlightDirectContribution(P, N, i, V, nDotV, diffuse, F0, roughness);
      }

      // environment lighting
      if (false)
      {
         // L is V reflection based on normal.
         float3 L = -V + 2.0f * dot(V, N) * N;
         float3 unattenuatedLightContribution = pbrComputeUnattenuatedLightContribution(P, N, L, V, nDotV, diffuse, F0, roughness);
         colorAccumulator += unattenuatedLightContribution * 0.05f;
      }

      // Don't support variable exposure yet, so just hardcoded this.
      colorAccumulator *= 4.0f;

      // Monitor gamma decodes with x^2.2, so invert that here (gamma encoding/expansion)
      // to achieve linear colorspace. Alternatively could use sRGB backbuffer colorspace.
      // http://www.codinglabs.net/article_gamma_vs_linear.aspx
   } else {
      // Traditional diffuse lighting (diffuse is base)
      if (!shadowTestEnabled) {
         colorAccumulator = base;
      } else {
         for (uint i = 0; i != numSpotlights; i++) {
            float3 lighting = computeSpotlightLighting(input.positionWorld, input.normalWorld, ShadowMaps, SpotlightDescriptions[i]);
            colorAccumulator += base * lighting;
         }
      }
   }

   return float4(pow(colorAccumulator, 1.0f / 2.2f), transparency);
}