using Ff.DevSuite;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

internal class ManualInitialization : MonoBehaviour
{
    private void Awake()
    {
        DevSuiteContext.Default.Initialize(this, new List<Assembly>());
        DevSuiteCommandsTesting.RegisterAll(DevSuiteContext.Default);
    }
}