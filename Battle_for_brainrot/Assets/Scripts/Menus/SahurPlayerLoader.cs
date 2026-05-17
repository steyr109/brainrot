using UnityEngine;

public class SahurPlayerLoader : MonoBehaviour
{
    public GameObject defaultPlayer;
    public GameObject sahurPrefab;

    public Transform spawnPoint;

    void Start()
    {
        bool useSahur = PlayerPrefs.GetInt("UseSahur", 0) == 1;

        if (!useSahur)
            return;

        if (defaultPlayer != null)
            Destroy(defaultPlayer);

        Instantiate(sahurPrefab, spawnPoint.position, Quaternion.identity);
    }
}