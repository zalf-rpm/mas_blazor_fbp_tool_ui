using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Mas.Schema.Registry;
using Restorer = Mas.Infrastructure.Common.Restorer;

namespace BlazorDrawFBP.Shared
{
    public class Shared
    {
        public static readonly ulong ChannelStarterInterfaceId = typeof(Mas.Schema.Fbp.IStartChannelsService)
            .GetCustomAttribute<Capnp.TypeIdAttribute>(false)?.Id ?? 0;
        public static readonly ulong RegistryInterfaceId = typeof(Mas.Schema.Registry.IRegistry)
            .GetCustomAttribute<Capnp.TypeIdAttribute>(false)?.Id ?? 0;

        public Dictionary<string, Mas.Schema.Registry.IRegistry> ServiceId2Registries { get; } = [];
        public Dictionary<string, (string, string)> RegistryServiceIdToPetNameAndSturdyRef { get; } = [];

        public Dictionary<string, Mas.Schema.Fbp.IStartChannelsService> ServiceId2ChannelStarterServices { get; } = [];
        public Dictionary<string, (string, string)> ChannelServiceIdToPetNameAndSturdyRef { get; } = [];

        public Dictionary<ulong, System.Type> InterfaceIdToType { get; } = new () {
            { RegistryInterfaceId, typeof(Mas.Schema.Registry.IRegistry) },
            { ChannelStarterInterfaceId, typeof(Mas.Schema.Fbp.IStartChannelsService) },
        };

        public Dictionary<string, Proxy> SturdyRef2Services { get; } = [];

        public Mas.Schema.Fbp.IStartChannelsService CurrentChannelStarterService =>
            ServiceId2ChannelStarterServices.FirstOrDefault(new KeyValuePair<string, IStartChannelsService>("none", null)).Value;

        public readonly Dictionary<string, HashSet<(string, string)>> CatId2CompServiceIdAndComponentIds = new();
        public readonly Dictionary<string, Mas.Schema.Common.IdInformation> CatId2Info = new();
        public readonly Dictionary<(string, string), Mas.Schema.Fbp.Component> ServiceIdAndComponentId2Component = new();

        public static ulong GetInterfaceId<T>()
            where T : class
        {
            var x = typeof(T);
            return typeof(T).GetCustomAttribute<Capnp.TypeIdAttribute>(false)?.Id ?? 0;
        }

        public static string MakeUniqueKey<T>(Dictionary<string, T> dict, string key)
        {
            var key2 = key;
            while (dict.ContainsKey(key))
            {
                if (!int.TryParse(key2[^1..^1], out var i)) key += 2;
                if (i < 9)
                {
                    key2 = key2[..^1] + (i + 1);
                }
                else
                {
                    if (int.TryParse(key2[^2..^1], out var i2))
                    {
                        key2 = key2[..^2] + (i2 + 1);
                    }
                }
            }

            return key2;
        }

        public async Task<IStartChannelsService> ConnectToStartChannelsService(
            ConnectionManager conMan,
            string petName,
            string sturdyRef)
        {
            try
            {
                var service = await conMan.Connect<Mas.Schema.Fbp.IStartChannelsService>(sturdyRef);
                if (service == null) return null;
                var info = await service.Info();
                Console.WriteLine("Connected to channel starter service @ " + sturdyRef);
                //var iid = GetInterfaceId<IStartChannelsService>();
                var petName2 = MakeUniqueKey(ServiceId2ChannelStarterServices, petName ?? "chan_start_serv");
                ServiceId2ChannelStarterServices[info.Id] = Capnp.Rpc.Proxy.Share(service);
                SturdyRef2Services[sturdyRef] = Capnp.Rpc.Proxy.Share(service) as Proxy;
                ChannelServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
                return service;
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to channel starter service @ " + sturdyRef);
            }
            return null;
        }

        public async Task<Mas.Schema.Registry.IRegistry> ConnectToRegistryService(
            ConnectionManager conMan,
            string petName,
            string sturdyRef)
        {
            Mas.Schema.Registry.IRegistry reg = null;
            try
            {
                reg = await conMan.Connect<Mas.Schema.Registry.IRegistry>(sturdyRef);
                if  (reg == null) return null;
                var info = await reg.Info();
                Console.WriteLine("Connected to components registry @ " + sturdyRef);
                //var iid = GetInterfaceId<IRegistry>();
                var petName2 = MakeUniqueKey(ServiceId2Registries, petName ?? "reg_serv");
                ServiceId2Registries[info.Id] = Capnp.Rpc.Proxy.Share(reg);
                SturdyRef2Services[sturdyRef] = Capnp.Rpc.Proxy.Share(reg) as Proxy;
                RegistryServiceIdToPetNameAndSturdyRef[info.Id] = (petName2, sturdyRef);
                Console.WriteLine("added petName2: " + petName2 + " and sturdyRef: " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to components registry @ " + sturdyRef);
                return null;
            }

            await LoadComponentsFromRegistry(reg, sturdyRef);
            return reg;
        }

        public async Task LoadComponentsFromRegistry(Mas.Schema.Registry.IRegistry reg, string sturdyRef)
        {
            if (reg ==  null) return;
            try
            {
                var categories = await reg.SupportedCategories();
                foreach (var cat in categories)
                {
                    if (!CatId2Info.ContainsKey(cat.Id))
                        CatId2Info[cat.Id] = new IdInformation
                        {
                            Id = cat.Id, Name = cat.Name ?? cat.Id,
                            Description = cat.Description ?? cat.Name ?? cat.Id
                        };
                }
                Console.WriteLine("Loaded supported categories from " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Error loading supported categories from " + sturdyRef);
            }

            try
            {
                var info = await reg.Info();
                var entries = await reg.Entries(null);
                foreach (var e in entries)
                {
                    if (!CatId2CompServiceIdAndComponentIds.ContainsKey(e.CategoryId)) CatId2CompServiceIdAndComponentIds[e.CategoryId] = [];
                    CatId2CompServiceIdAndComponentIds[e.CategoryId].Add((info.Id, e.Id));
                    if (e.Ref is not Proxy p) continue;
                    var holder = p.Cast<Mas.Schema.Common.IIdentifiableHolder<Mas.Schema.Fbp.Component>>(true);
                    try
                    {
                        ServiceIdAndComponentId2Component.Add((info.Id, e.Id), await holder.Value());
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
                Console.WriteLine("Loaded entries from " + sturdyRef);
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Error loading entries from " + sturdyRef);
            }
        }

        public static string NodeNameFromPort(PortModel port)
        {
            return port.Parent switch
            {
                CapnpFbpComponentModel m => m.ProcessName,
                CapnpFbpIipModel m2 => m2.Id,
                _ => "unknown_process"
            };
        }

        public static string PortName(PortModel port)
        {
            return port switch
            {
                CapnpFbpPortModel p => p.Name,
                CapnpFbpIipPortModel => "IIP",
                _ => "unknown_port"
            };
        }

        public static Task CreateChannel(Mas.Infrastructure.Common.ConnectionManager conMan,
            Mas.Schema.Fbp.IStartChannelsService css, PortModel outPort, CapnpFbpPortModel inPort)
        {
            if (css == null) return Task.CompletedTask;

            var t = Task.Run(async () =>
            {
                if (inPort.Channel == null) // there is no channel for the IN port yet
                {
                    var si = await css.Start(new StartChannelsService.Params
                    {
                        Name = $"{NodeNameFromPort(outPort)}.{PortName(outPort)}->" +
                               $"{NodeNameFromPort(inPort)}.{PortName(inPort)}"
                    });
                    if (si.Item1.Count <= 0 || si.Item1[0].ReaderSRs.Count <= 0 || si.Item1[0].WriterSRs.Count <= 0) return;
                    switch (outPort)
                    {
                        case CapnpFbpPortModel sPort:
                            sPort.ReaderWriterSturdyRef = si.Item1[0].WriterSRs[0];
                            break;
                        case CapnpFbpIipPortModel iipPort:
                            iipPort.WriterSturdyRef = si.Item1[0].WriterSRs[0];
                            break;
                    }

                    inPort.ReaderWriterSturdyRef = si.Item1[0].ReaderSRs[0];
                    // attach channel cap to IN port (target port)
                    inPort.Channel =
                        await conMan.Connect<Mas.Schema.Fbp.IChannel<Mas.Schema.Fbp.IP>>(si.Item1[0].ChannelSR);
                    // attach stop channel cap to IN port
                    inPort.StopChannel = si.Item2;
                }
                else
                {
                    var writerSr =
                        Restorer.SturdyRefStr((await inPort.Channel.Writer().Result.Save(null)).SturdyRef);
                    switch (outPort)
                    {
                        case CapnpFbpPortModel sPort:
                            sPort.ReaderWriterSturdyRef = writerSr;
                            break;
                        case CapnpFbpIipPortModel iipPort:
                            iipPort.WriterSturdyRef = writerSr;
                            break;
                    }

                    Debug.Assert(
                        !string.IsNullOrEmpty(inPort
                            .ReaderWriterSturdyRef)); // = Restorer.SturdyRefStr((await eps.Item1.Save(null)).SturdyRef));
                }
            });
            switch (outPort)
            {
                case CapnpFbpPortModel sPort:
                    sPort.ChannelTask = t;
                    break;
                case CapnpFbpIipPortModel iipPort:
                    iipPort.ChannelTask = t;
                    break;
            }

            inPort.ChannelTask = t;
            return t;
        }
    }
}