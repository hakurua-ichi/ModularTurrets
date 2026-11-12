using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Verse;

namespace ModularTurrets;

public sealed class ModLanguageDef : Def
{
    public string folder;
    public string gameLanguage;
    public int order;
    public bool IsAutomatic => string.Equals(gameLanguage, "auto", StringComparison.OrdinalIgnoreCase);
}
