using System;
using UnityEngine;

public static class ExtensionMethods
{
  
   public static Vector3 XYO(this Vector3 vector3)
   {
      vector3.z = 0;
      return vector3;
   }
   public static Vector3 XY(this Vector3 vector3)
   {
      return new Vector2(vector3.x, vector3.y);
   }
   public static Vector3 OYO(this Vector3 vector3)
   {
      vector3.x = 0;
      vector3.z = 0;
      return vector3;
   }
   public static Vector3 OOZ(this Vector3 vector3)
   {
      vector3.x = 0;
      vector3.y = 0;
      return vector3;
   }
   public static Vector3 XOY(this Vector3 vector3)
   {
      vector3.y = 0;
      return vector3;
   }
   public static void SetActive(this GameObject[] objects,bool  active)
   {
      foreach (var obj in objects)
      {
         obj.SetActive(active);
      }
   }
}
