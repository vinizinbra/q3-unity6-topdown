using TMPro;
using UnityEngine;

public class VictoryTab : TabContent
{
    [SerializeField] private TMP_Text placementText;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private GameObject firstPlacesParticle;
    [SerializeField] private int placement;

    void Start()
    {
    }


    void Update()
    {
    }

    protected override void OnShow()
    {
        
    }

    protected override void OnHide()
    {
    }

    public void Setup(int placement)
    {
        placementText.text = $"YOU ARE #{placement}!";
        rankText.text = $"RANK: #{placement}";
        
        rankText.gameObject.SetActive(placement != 1);
        placementText.gameObject.SetActive(placement == 1);
        firstPlacesParticle.gameObject.SetActive(placement == 1);
        this.placement = placement-1;
    }
}
