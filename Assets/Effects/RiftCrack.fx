//裂隙恶灵体表着色器:蓝紫染色(顶点色)+ 流动裂隙纹 + 内缘辉光 + 白化闪(沿用 aweffect 的 a/alpha 语义)
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

float a;              //白化量(受击闪/入场闪)
float alpha;          //整体透明度
float time;           //裂纹缓慢流动
float crackStrength;  //裂纹亮度
float4 crackColor;    //裂纹光色(alpha 置 0,预乘混合下等效加法)
float4 rimColor;      //内缘辉光色(alpha 置 0)
float2 pixel;         //1/贴图尺寸,边缘检测步长

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 base = tex2D(uImage0, uv);
    if (!any(base))
    {
        return float4(0, 0, 0, 0);
    }

    //双层噪声反向缓流,脊线谷值成窄裂纹
    float n1 = tex2D(noiseSampler, uv * 1.6 + float2(time * 0.030, time * 0.011)).r;
    float n2 = tex2D(noiseSampler, uv * 2.3 - float2(time * 0.017, time * 0.026)).r;
    float vein = abs(n1 + n2 - 1.0);
    float crack = saturate(1.0 - vein * 6.0);
    crack = crack * crack * crackStrength * base.a;

    //四方向 alpha 差 = 内缘,呼吸微脉动
    float edge = 0;
    edge += 1 - tex2D(uImage0, uv + float2(pixel.x, 0)).a;
    edge += 1 - tex2D(uImage0, uv - float2(pixel.x, 0)).a;
    edge += 1 - tex2D(uImage0, uv + float2(0, pixel.y)).a;
    edge += 1 - tex2D(uImage0, uv - float2(0, pixel.y)).a;
    edge = saturate(edge * 0.5) * base.a * (0.75 + 0.25 * sin(time * 2.4));

    float4 col = base * input.Color;
    col = lerp(col, float4(1, 1, 1, 1) * base.a * input.Color.a, a);
    col += crackColor * crack;
    col += rimColor * edge;
    return col * alpha;
}

technique Technique1
{
    pass RiftCrackPass
    {
        PixelShader = compile ps_3_0 MainPS();
    }
};
