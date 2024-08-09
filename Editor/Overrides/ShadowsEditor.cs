using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(Shadows))]
    sealed class ScreenSpaceShadowsEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Penumbra;
        SerializedDataParameter m_Intensity;

        SerializedDataParameter m_ShadowScatterMode;
        SerializedDataParameter m_OcclusionPenumbra;
        SerializedDataParameter m_ShadowRampTex;
        SerializedDataParameter m_ScatterR;
        SerializedDataParameter m_ScatterG;
        SerializedDataParameter m_ScatterB;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Shadows>(serializedObject);

            m_Penumbra = Unpack(o.Find(x => x.penumbra));
            m_Intensity = Unpack(o.Find(x => x.intensity));

            m_ShadowScatterMode = Unpack(o.Find(x => x.shadowScatterMode));
            m_OcclusionPenumbra = Unpack(o.Find(x => x.occlusionPenumbra));
            m_ShadowRampTex = Unpack(o.Find(x => x.shadowRampTex));
            m_ScatterR = Unpack(o.Find(x => x.scatterR));
            m_ScatterG = Unpack(o.Find(x => x.scatterG));
            m_ScatterB = Unpack(o.Find(x => x.scatterB));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Penumbra);
            PropertyField(m_Intensity);

            EditorGUILayout.Space(10);

            if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.RampTexture)
            {
                PropertyField(m_ShadowScatterMode);
                PropertyField(m_OcclusionPenumbra);
                PropertyField(m_ShadowRampTex);
            }
            else if (m_ShadowScatterMode.value.intValue == (int)ShadowScatterMode.SubSurface)
            {
                PropertyField(m_ShadowScatterMode);
                PropertyField(m_OcclusionPenumbra);
                PropertyField(m_ScatterR);
                PropertyField(m_ScatterG);
                PropertyField(m_ScatterB);
            }
            else
            {
                PropertyField(m_ShadowScatterMode);
            }

        }
    }
}