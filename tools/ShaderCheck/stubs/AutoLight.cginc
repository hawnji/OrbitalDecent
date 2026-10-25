#ifndef AUTOLIGHT_STUB
#define AUTOLIGHT_STUB
#define SHADOW_COORDS(i) float4 _ShadowCoord : TEXCOORD##i;
#define TRANSFER_SHADOW(o) o._ShadowCoord = o.pos;
#define SHADOW_ATTENUATION(i) (i._ShadowCoord.w > -1e9 ? 1.0 : 0.0)
#endif
