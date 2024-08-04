using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(ScreenSpaceShadows))]
    sealed class ScreenSpaceShadowsEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Penumbra;
        SerializedDataParameter m_Intensity;
        SerializedDataParameter m_EnableShadowRamp;
        SerializedDataParameter m_ShadowRampTex;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceShadows>(serializedObject);

            m_Penumbra = Unpack(o.Find(x => x.penumbra));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_EnableShadowRamp = Unpack(o.Find(x => x.enableShadowRamp));
            m_ShadowRampTex = Unpack(o.Find(x => x.shadowRampTex));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Penumbra);
            PropertyField(m_Intensity);

            EditorGUILayout.Space(10);
            // ShadowRamp
            {
                PropertyField(m_EnableShadowRamp);
                bool guiEnableOri = GUI.enabled;
                if (!m_EnableShadowRamp.value.boolValue)
                {
                    GUI.enabled = false;
                }
                PropertyField(m_ShadowRampTex);
                GUI.enabled = guiEnableOri;
            }

        }
    }
}