using System.Diagnostics;
using System.Threading.Tasks;
using Blazor.Diagrams.Core.Models;
using BlazorDrawFBP.Models;
using Mas.Schema.Fbp;
using Restorer = Mas.Infrastructure.Common.Restorer;

namespace BlazorDrawFBP.Shared
{
    public struct Shared
    {
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
                    var si = await css.Create(new StartChannelsService.Params
                    {
                        Name = $"{NodeNameFromPort(outPort)}.{PortName(outPort)}->" +
                               $"{NodeNameFromPort(inPort)}.{PortName(inPort)}"
                    });
                    if (si.Count <= 0 || si[0].ReaderSRs.Count <= 0 || si[0].WriterSRs.Count <= 0) return;
                    switch (outPort)
                    {
                        case CapnpFbpPortModel sPort:
                            sPort.ReaderWriterSturdyRef = si[0].WriterSRs[0];
                            break;
                        case CapnpFbpIipPortModel iipPort:
                            iipPort.WriterSturdyRef = si[0].WriterSRs[0];
                            break;
                    }

                    inPort.ReaderWriterSturdyRef = si[0].ReaderSRs[0];
                    // attach channel cap to IN port (target port)
                    inPort.Channel =
                        await conMan.Connect<Mas.Schema.Fbp.IChannel<Mas.Schema.Fbp.IP>>(si[0].ChannelSR);
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