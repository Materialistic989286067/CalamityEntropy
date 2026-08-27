// CalamityEntropy:TrailStreak
// 行为等效替代灾厄同名旧键(FadedUVMapStreak)的自有实现: 通用 UV 淡出条带拖尾
// 参数契约 = 原版 MiscShaderData 标准通道; 条带贴图经 SetShaderTexture 落在 uImage1
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

// x = 拖尾进度(0 头部/1 尾端), y = 横截面
float4 PixelFunc(VSOutput input) : COLOR0
{
    float4 color = input.Color;
    float2 coords = input.TexCoord.xy;

    // 还原梯形分段的纵向畸变
    coords.y = (coords.y - 0.5) / input.TexCoord.z + 0.5;

    // 条带贴图沿进度轴滚动采样, 红通道作透明度
    float streakAlpha = tex2D(uImage1, float2(frac(coords.x - uTime * 2.5), coords.y)).r;

    // 头部用窄的正弦亮带, 越靠尾端越信任贴图蒙版; 幂次随进度收紧到放宽
    float edgePower = lerp(3, 10, coords.x);
    float opacity = lerp(pow(sin(coords.y * 3.141), edgePower), streakAlpha, coords.x);

    // 末端 30% 快速衰减收尾
    if (coords.x > 0.7)
        opacity *= pow(1 - (coords.x - 0.7) / 0.3, 6);

    return color * opacity * 1.5;
}

technique Technique1
{
    pass TrailPass
    {
        VertexShader = compile vs_2_0 VertexFunc();
        PixelShader = compile ps_2_0 PixelFunc();
    }
}
