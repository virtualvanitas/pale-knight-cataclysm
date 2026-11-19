using SoulsFormats;
using System.Collections.Generic;
using System.Numerics;

namespace HKX2
{
    public partial class hkReferencedObject : hkBaseObject
    {
        public override uint Signature { get => 3071048009; }
        
        
        public override void Read(PackFileDeserializer des, BinaryReaderEx br)
        {
            base.Read(des, br);
            if (HavokLayout.Bb) br.ReadUInt32(); else br.ReadUInt64();
        }
        
        public override void Write(PackFileSerializer s, BinaryWriterEx bw)
        {
            base.Write(s, bw);
            if (HavokLayout.Bb) bw.WriteUInt32(0); else bw.WriteUInt64(0);
        }
    }
}
