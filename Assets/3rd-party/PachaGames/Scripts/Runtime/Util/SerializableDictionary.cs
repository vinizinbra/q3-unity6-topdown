using System;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;

[Serializable]
public class StringSpritePair
{
    public string key;
    [ShowAssetPreview]
    public Sprite sprite;
}

[Serializable]
public class StringSpriteDictionary
{
    [SerializeField]
    private List<StringSpritePair> list = new List<StringSpritePair>();
    
    private Dictionary<string, Sprite> dictionary;

    public void BuildDictionary()
    {
        dictionary = new Dictionary<string, Sprite>();
        foreach (var spritePair in list)
        {
            dictionary.Add(spritePair.key, spritePair.sprite);
        }
    }
    public Sprite this[string key]
    {
        get
        {
            if(dictionary == null)
                BuildDictionary();
            return dictionary[key];
        }
        set => dictionary[key] = value;
    }

    public bool TryGetValue(string key, out Sprite sprite)
    {
        return dictionary.TryGetValue(key, out sprite);
    }
    
    public bool ContainsKey(string key)
    {
        return dictionary.ContainsKey(key);
    }
    
    public void Add(string key, Sprite sprite)
    {
        dictionary[key] = sprite;
    }
    
}