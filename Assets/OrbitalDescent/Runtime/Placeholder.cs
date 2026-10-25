using UnityEngine;
using UnityEngine.UI;
namespace OrbitalDescent { public class Placeholder : MonoBehaviour { Text t; void Start() { t = gameObject.AddComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } } }
