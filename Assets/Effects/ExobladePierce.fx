// CalamityEntropy:ExobladePierce
// 行为等效替代灾厄同名旧键(ExobladePierceShader)的自有实现: 能量突刺拖尾
// 调用姿势: SetShaderTexture->uImage1(噪声), UseImage2->uImage2(亮度条带), UseColor/UseSecondaryColor 双色
sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
sampler uImage2 : register(s2);
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
float2 uImageSize2;
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

    // 还原梯形分段的纵向畸变
    coords.y = (coords.y - 0.5) / input.TexCoord.z + 0.5;

    // 中线高幂泛光带: 只留很窄的能量核心
    float bloom = pow(sin(coords.y * 3.141), 5.6);

    // 噪声图快速回卷, 驱动双色混合与亮度扰动
    float noise = tex2D(uImage1, coords * 3 - float2(uTime * 2.44, 0));

    // 亮度条带图横向拉伸滚动, 叠加噪声高光
    float brightStreak = tex2D(uImage2, coords * float2(2, 1) - float2(uTime * 1.61, 0)) + noise * bloom;

    // 主色与副色按噪声插值出能量色
    float4 energyColor = float4(lerp(uColor, uSecondaryColor, noise), 1);

    // 顶点 alpha 控制整体透明度, 尾端按 1.6 次幂淡出
    return (energyColor * bloom + brightStreak * bloom) * color.a * pow(1 - coords.x, 1.6);
}

technique Technique1
{
    pass PiercePass
    {
        VertexShader = compile vs_2_0 VertexFunc();
        PixelShader = compile ps_2_0 PixelFunc();
    }
}
