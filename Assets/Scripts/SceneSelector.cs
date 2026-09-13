using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class SceneSelector : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ManifestSceneLoader sceneLoader;

    private List<string> sceneIds = new List<string>
    {
        "patch_43"
    };

    private void Start()
    {
        dropdown.ClearOptions();
        dropdown.AddOptions(sceneIds);

        dropdown.onValueChanged.AddListener(OnSceneSelected);
    }

    private void OnSceneSelected(int index)
    {
        string sceneId = sceneIds[index];

        Debug.Log("Scene selected: " + sceneId);

        sceneLoader.Load(sceneId);
    }
}