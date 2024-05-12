using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    [CustomEditor(typeof(ProceduralToonSky))]
    sealed class ProceduralToonSkyEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_Sky;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ProceduralToonSky>(serializedObject);

        }

        public override void OnInspectorGUI()
        {


        }
    }
}
