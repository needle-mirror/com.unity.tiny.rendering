$input a_position, a_color0, a_texcoord0
$output v_color0, v_texcoord0

#include "../common/common.sh"

uniform vec4 u_texmad;

void main()
{
	gl_Position = mul(u_modelViewProj, vec4(a_position, 1.0) );
	v_color0 = a_color0;
    v_texcoord0 = transformTex(a_texcoord0, u_texmad);
}
