using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using Capnp.Rpc;
using Mas.Infrastructure.Common;
using Mas.Schema.Common;
using Mas.Schema.Fbp;
using Restorer = Mas.Infrastructure.Common.Restorer;

namespace BlazorDrawFBP.Shared
{
    public class Shared
    {
        public List<Mas.Schema.Registry.IRegistry> Registries { get; } = [];

        public List<Mas.Schema.Fbp.IStartChannelsService> ChannelStarterServices { get; } = [];

        public Mas.Schema.Fbp.IStartChannelsService CurrentChannelStarterService => ChannelStarterServices.FirstOrDefault(null as IStartChannelsService);

        public readonly Dictionary<string, List<string>> CatId2ComponentIds = new();
        public readonly Dictionary<string, Mas.Schema.Common.IdInformation> CatId2Info = new();
        public readonly Dictionary<string, Mas.Schema.Fbp.Component> ComponentId2Component = new();

        public async Task<IStartChannelsService> ConnectToStartChannelsService(ConnectionManager conMan, string sturdyRef)
        {
            try
            {
                var service = await conMan.Connect<Mas.Schema.Fbp.IStartChannelsService>(sturdyRef);
                if (service == null) return null;
                Console.WriteLine("Connected to channel starter service @ " + sturdyRef);
                ChannelStarterServices.Add(Capnp.Rpc.Proxy.Share(service));
                return service;
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to channel starter service @ " + sturdyRef);
            }
            return null;
        }

        public async Task<Mas.Schema.Registry.IRegistry> ConnectToRegistryService(ConnectionManager conMan, string sturdyRef)
        {
            Mas.Schema.Registry.IRegistry reg = null;
            try
            {
                reg = await conMan.Connect<Mas.Schema.Registry.IRegistry>(sturdyRef);
                if  (reg == null) return null;
                Console.WriteLine("Connected to components registry @ " + sturdyRef);
                Registries.Add(Capnp.Rpc.Proxy.Share(reg));
            }
            catch (Capnp.Rpc.RpcException)
            {
                Console.WriteLine("Couldn't connect to components registry @ " + sturdyRef);
                return null;
            }
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
                var entries = await reg.Entries(null);
                foreach (var e in entries)
                {
                    if (!CatId2ComponentIds.ContainsKey(e.CategoryId)) CatId2ComponentIds[e.CategoryId] = [];
                    CatId2ComponentIds[e.CategoryId].Add(e.Id);
                    if (e.Ref is not Proxy p) continue;
                    var holder = p.Cast<Mas.Schema.Common.IIdentifiableHolder<Mas.Schema.Fbp.Component>>(true);
                    try
                    {
                        ComponentId2Component.Add(e.Id, await holder.Value());
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
            return reg;
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