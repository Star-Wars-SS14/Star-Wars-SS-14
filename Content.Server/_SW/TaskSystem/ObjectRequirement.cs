using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Server._SW.TaskSystem;

[Serializable, DataDefinition]
public sealed partial class ObjectRequirement
{
    [DataField]
    public int Number = 0;

    [DataField("objects")]
    public List<string> ObjectsPrototypes = new List<string>();
    public ObjectRequirement(int num, List<string> protolist)
    {
        Number = num;
        ObjectsPrototypes = protolist;
    }
}
