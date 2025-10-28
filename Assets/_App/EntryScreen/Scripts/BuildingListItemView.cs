using Shared.Scripts.Geo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
    public class BuildingListItemView : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private RawImage thumbnail;
        [SerializeField] private TextMeshProUGUI title;
        [SerializeField] private TextMeshProUGUI subtitle;
        [SerializeField] private Button openGeoPortalButton;
        [SerializeField] private Button openMapsButton;
        [SerializeField] private Button openARButton;

        private ProjectedBuilding _data;

        public void Bind(ProjectedBuilding data, int index, System.Action<ProjectedBuilding> onOpenGeoPortal, System.Action<ProjectedBuilding> onOpenMaps, System.Action<ProjectedBuilding> onOpenAR)
        {
            Debug.Log("Called with data: " + (data != null ? data.Egid : "null" ) + ", index: " + index);
            _data = data;
            
            // Log if text components are set
            Debug.Log("Title component is " + (title ? "set" : "null"));
            Debug.Log("Subtitle component is " + (subtitle ? "set" : "null"));

            // Simple title/subtitle for now
            if (title)
            {
                if (string.IsNullOrWhiteSpace(_data.GebHauptNutzung))
                {
                    title.SetText($"Building {index:00}");
                    _data.GebHauptNutzung = title.text; // Set the GebHauptNutzung to Building XX 
                }
                else
                {
                    title.SetText(_data.GebHauptNutzung);
                }
            }

            if (subtitle) subtitle.SetText(string.IsNullOrWhiteSpace(_data.Egid)
                ? (_data.Nbident ?? "")
                : $"EGID: {_data.Egid}  •  {_data.Nbident}");

            // Placeholder thumbnail (leave empty if you like)
            if (thumbnail) thumbnail.texture = null;
            ThumbnailService.Instance?.RequestThumbnail(_data, 250, tex => {
                if (!this || _data != data || !thumbnail || !tex) return;

                thumbnail.texture = tex;
                thumbnail.color = Color.white;
                thumbnail.SetNativeSize();
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    thumbnail.transform as RectTransform); 

            });

            // Wire buttons
            if (openGeoPortalButton)
            {
                openGeoPortalButton.onClick.RemoveAllListeners();
                openGeoPortalButton.onClick.AddListener(() => onOpenGeoPortal?.Invoke(_data));
            }
            if (openMapsButton)
            {
                openMapsButton.onClick.RemoveAllListeners();
                openMapsButton.onClick.AddListener(() => onOpenMaps?.Invoke(_data));
            }
            if (openARButton)
            {
                openARButton.onClick.RemoveAllListeners();
                openARButton.onClick.AddListener(() => onOpenAR?.Invoke(_data));
            }
        }
    }
