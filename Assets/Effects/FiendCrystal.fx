//虚空水晶棱光着色器:噪声伪折射(R/B 错位色散)+ 内部辉光呼吸 + 斜向棱面扫光
sampler uImage0 : register(s0);
texture2D noiseTex;
sampler noiseSampler = sampler_state
{
    Texture = <noiseTex>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

float time;
float seed;        //每颗水晶相位差,防同步闪烁
float alpha;
float glowPulse;   //内辉光强度(牢笼收缩期抬升)
float4 glowColor;  //内辉光色(alpha 置 0)

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 c0 = tex2D(uImage0, uv);
    if (!any(c0))
    {
        return float4(0, 0, 0, 0);
    }

    //伪折射:慢流噪声驱动 uv 扰动,R/B 通道错位取样出色散
    float2 flow = tex2D(noiseSampler, uv * 1.5 + float2(time * 0.05 + seed, seed * 0.7)).rg - 0.5;
    float2 shift = flow * 0.035;
    float rr = tex2D(uImage0, uv + shift).r;
    float bb = tex2D(uImage0, uv - shift).b;
    float4 base = float4(rr, c0.g, bb, c0.a);

    //内部辉光:噪声呼吸,乘体积 alpha
    float inner = tex2D(noiseSampler, uv * 2.0 + float2(seed, -time * 0.04)).r;

    //棱面扫光:斜向亮带周期掠过
    float band = frac((uv.x + uv.y) * 1.1 - time * 0.33 - seed);
    float gleam = saturate(1 - abs(band - 0.5) * 8);
    gleam = pow(gleam, 6);

    float4 col = base * input.Color;
    col += glowColor * (inner * glowPulse * c0.a);
    col += float4(1, 1, 1, 0) * (gleam * c0.a * (0.5 + glowPulse * 0.5));
    return col * alpha;
}

technique Technique1
{
    pass FiendCrystalPass
    {
        PixelShader = compile ps_3_0 MainPS();
    }
};
