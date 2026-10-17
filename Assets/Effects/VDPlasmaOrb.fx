//方形图的 coords 从 0 到 1,中心在 0.5,p 按半边长归一,球壳半径是 uRadius,壳外到 1.0 是辉光,输出预乘 alpha 并用 AlphaBlend 混合,没有动态分支
//s1 是 SwirlNoise(实测 0 到 0.88,均值 0.2)给流丝,s2 是 GradientNoise(0.09 到 0.93)给球内团块;球面取样坐标是 p 除以深度,不用 atan2,没有接缝
sampler uImage0 : register(s0);
sampler uImage1 : register(s1);
sampler uImage2 : register(s2);

float uTime;
float uCharge;//该值是蓄力比,0 到 1,越满流丝越密越亮、流得越快
float uOpacity;
float uRadius;//该值是球壳半径,按半边长归一,0.4 到 0.7
float uSeed;
float3 uColorMain;//该值是等离子主色
float3 uColorCore;//该值是白热芯色
float3 uColorDeep;//该值是球内深处的暗色

float4 PixelFunc(float4 baseColor : COLOR0, float2 coords : TEXCOORD0) : COLOR0
{
    float2 p = (coords - 0.5) * 2.0;
    float r = length(p);
    float rad = max(uRadius, 0.05);
    float x = r / rad;
    float inside = 1.0 - smoothstep(0.96, 1.0, x);
    float z = sqrt(saturate(1.0 - x * x));

    //球面取样:越靠边越压缩,两层流丝反向绕行,读作球里翻滚的放电
    float2 sp = (p / rad) / (0.35 + 0.65 * z);
    float spin = uTime * (0.22 + 0.55 * uCharge);
    float2 seedOff = float2(uSeed * 0.37, uSeed * 0.61);
    float fil1 = tex2D(uImage1, frac(sp * 0.34 + float2(spin * 0.4, spin * 0.13) + seedOff)).r;
    float fil2 = tex2D(uImage1, frac(sp * float2(-0.46, 0.46) + float2(-spin * 0.31, spin * 0.22) + seedOff + 0.5)).r;
    float fil = max(fil1, fil2 * 0.9);
    float clump = tex2D(uImage2, frac(sp * 0.28 + float2(spin * 0.18, -spin * 0.14) + seedOff * 1.3)).r;

    //球内:暗底 + 团块 + 流丝 + 高斯白热芯;细电流交给几何电弧画在球上,这里只做成片的等离子流
    float filGain = smoothstep(0.1 + 0.16 * (1.0 - uCharge), 0.62, fil);
    float coreGlow = exp(-x * x / (0.12 + 0.18 * uCharge));
    float3 body = uColorDeep * (0.55 + 0.6 * clump)
        + uColorMain * (clump * 0.3 + filGain * (0.75 + 0.85 * uCharge)) * (0.35 + 0.65 * z)
        + uColorCore * (coreGlow * (0.55 + 0.75 * uCharge) + filGain * filGain * 0.55 * z);

    //菲涅尔壳与一道贴边细线
    float fres = pow(1.0 - z, 3.0);
    float lineD = (x - 0.975) / 0.022;
    float shellLine = exp(-lineD * lineD);
    body += uColorMain * fres * 1.1 + uColorCore * shellLine * (0.45 + 0.35 * uCharge);
    float bodyA = inside * (0.8 + 0.2 * filGain);

    //壳外辉光:只发光不遮挡
    float halo = exp(-max(x - 1.0, 0.0) * 3.5) * (1.0 - inside) * (0.3 + 0.45 * uCharge);
    float guard = 1.0 - smoothstep(0.88, 1.0, r);

    float3 col = body * inside + uColorMain * halo;
    float k = uOpacity * guard;
    return float4(col * k, bodyA * k) * baseColor;
}

technique Technique1
{
    pass OrbPass
    {
        PixelShader = compile ps_3_0 PixelFunc();
    }
}
