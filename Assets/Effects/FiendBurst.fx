//虚熵魔物熵爆全屏着色器:蓄力紫涨(暗角向爆心收紧)+ 静默拍去饱和 + 冲击帧对比度
sampler uImage0 : register(s0);

float progress;   //0~1 蓄力进度
float desat;      //0~1 去饱和(静默拍 / 冲击帧)
float contrast;   //冲击帧对比度抬升
float2 center;    //爆心屏幕 uv
float aspect;     //宽高比校正
float4 tintColor; //紫涨色(a = 罩染强度)

float4 PSFunction(float2 coords : TEXCOORD0) : COLOR0
{
    float4 col = tex2D(uImage0, coords);
    float2 d = coords - center;
    d.x *= aspect;
    float dist = length(d);

    float grey = dot(col.rgb, float3(0.299, 0.587, 0.114));

    //全场紫涨:越靠屏幕边缘越浓,随 progress 向爆心收紧
    float vig = saturate(dist * (1.1 + progress * 0.9) - 0.22);
    float3 tinted = tintColor.rgb * (grey * 0.85 + 0.15);
    col.rgb = lerp(col.rgb, tinted, vig * progress * tintColor.a);

    //爆心微光渗出
    col.rgb += tintColor.rgb * saturate(1 - dist * 3) * progress * 0.22;

    //静默拍 / 冲击帧:去饱和 + 对比度
    col.rgb = lerp(col.rgb, float3(grey, grey, grey), desat);
    col.rgb = (col.rgb - 0.5) * (1 + contrast) + 0.5;
    return col;
}

technique Technique1
{
    pass FiendBurstPass
    {
        PixelShader = compile ps_2_0 PSFunction();
    }
};
