// CalamityEntropy:ArtAttack
// 行为等效替代灾厄同名旧键(ArtAttackTrail)的自有实现: 全模组拖尾主力
// 参数契约 = 原版 MiscShaderData.Apply 写入的标准通道; 拖尾贴图经 SetShaderTexture 落在 uImage1
sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
float3 uColor;
float3 uSecondaryColor;
float uOpacity;
float uSaturation;
float uRotation;
float uTime;
float4 uSourceRect;
float2 uWorldPosition;
float uDirection;
float3 uLightSource;
float2 uImageSize0;
float2 uImageSize1;
matrix uWorldViewProjection;
float4 uShaderSpecificData;

struct VSInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float3 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float3 TexCoord : TEXCOORD0;
};

VSOutput VertexFunc(in VSInput input)
{
    VSOutput output = (VSOutput) 0;
    output.Position = mul(input.Position, uWorldViewProjection);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PixelFunc(VSOutput input) : COLOR0
{
    float4 color = input.Color;
    float2 coords = input.TexCoord.xy;

    // 图元梯形分段会挤压纵向 uv, 用第三分量还原
    coords.y = (coords.y - 0.5) / input.TexCoord.z + 0.5;

    // 淡出图沿拖尾方向回卷滚动, 红通道作透明度蒙版
    float streakAlpha = tex2D(uImage1, coords - float2(uTime * 0.6, 0)).r;

    // 中线泛光带: 两侧渐隐, 中心过曝
    float bloom = sin(coords.y * 3.141) * 1.4;

    // 头部(前 26%)从泛光带渐变到贴图蒙版
    float headBlend = saturate(coords.x / 0.26);
    return lerp(color * bloom, color * streakAlpha, headBlend);
}

technique Technique1
{
    pass TrailPass
    {
        VertexShader = compile vs_3_0 VertexFunc();
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
