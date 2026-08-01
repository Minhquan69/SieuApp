using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GLib;

namespace V3SClient.TLS
{
    public class RtspClientTlsInteraction : TlsInteraction
    {
        private TlsCertificate cert;
        private TlsCertificate caCertificate;

        public RtspClientTlsInteraction(TlsCertificate cert, TlsCertificate caCertificate)
        {
            this.cert = cert;
            this.caCertificate = caCertificate;
        }

        protected override TlsInteractionResult OnRequestCertificate(TlsConnection connection,
            TlsCertificateRequestFlags flags, Cancellable cancellable)
        {
            System.Diagnostics.Debug.WriteLine("Checking Cert");
           
            connection.AcceptCertificate += OnAcceptCertificate;
            connection.Certificate = this.cert;
            return TlsInteractionResult.Handled;
        }

        private void OnAcceptCertificate(object sender, AcceptCertificateArgs args)
        {
            Debug.WriteLine("Validating CA Cert");
            var peerCert = args.PeerCert;
            var errors = args.Errors;
            try
            {
                TlsCertificateFlags verifyFlags = peerCert.Verify(null, this.caCertificate);
                args.RetVal = (verifyFlags == 0);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Error verifying certificate chain: {e}");
                args.RetVal = false;
            }
        }

    }
}















