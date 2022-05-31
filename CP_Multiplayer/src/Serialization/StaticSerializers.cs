using MessagePack;
using MessagePack.Resolvers;
using UnityEngine;

namespace CPMod_Multiplayer.Serialization
{
    public static class StaticSerializers
    {
        private static bool registered = false;
        
        internal static void RegisterSerializers()
        {
            if (!registered)
            {
                StaticCompositeResolver.Instance.Register(
                    MPC.Resolvers.GeneratedResolver.Instance,
                    MessagePack.Resolvers.StandardResolver.Instance
                );
                var option = MessagePackSerializerOptions.Standard.WithResolver(StaticCompositeResolver.Instance);

                MessagePackSerializer.DefaultOptions = option;
                registered = true;
                
                Mod.logger.Log("Registered custom serializers");
            }
        }
    }
}