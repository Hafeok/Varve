// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>SigV4, web identity and the loopback rule of the S3 adapter (ADRs 0134, 0137).</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class S3CredentialTests
{
    [Fact]
    public void the_signature_is_aws_s_own_worked_example()
    {
        // "Example: GET Object" of the SigV4 header-signing documentation for S3.
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("https://examplebucket.s3.amazonaws.com/test.txt"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-9");
        SigV4.Sign(request, [], new SigningCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", null, DateTimeOffset.MaxValue), "us-east-1", new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero));

        string authorization = string.Join(",", request.Headers.GetValues("Authorization"));
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            authorization);
    }

    [Fact]
    public async Task web_identity_exchanges_the_token_file_and_keeps_the_credentials_until_five_minutes_before_they_expire()
    {
        ManualClock clock = ManualClock.Epoch();
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "oidc-token-one", T.Ct);
        int calls = 0;
        string? lastBody = null;
        DelegatingAnswer handler = new(async request =>
        {
            calls++;
            lastBody = await request.Content!.ReadAsStringAsync(T.Ct);
            string expiration = clock.Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<AssumeRoleWithWebIdentityResponse><AssumeRoleWithWebIdentityResult><Credentials>"
                    + "<AccessKeyId>ASIATEMP</AccessKeyId><SecretAccessKey>SECRET-THAT-MUST-NOT-APPEAR</SecretAccessKey>"
                    + "<SessionToken>SESSION-THAT-MUST-NOT-APPEAR</SessionToken><Expiration>" + expiration + "</Expiration>"
                    + "</Credentials></AssumeRoleWithWebIdentityResult></AssumeRoleWithWebIdentityResponse>",
                    Encoding.UTF8,
                    "text/xml"),
            };
        });
        using HttpClient http = new(handler);
        using WebIdentity identity = new(http, clock, new Uri("https://sts.example/"), file, "arn:aws:iam::1:role/varve", "varve");

        SigningCredentials first = await identity.GetAsync(T.Ct);
        Assert.Equal("ASIATEMP", first.AccessKeyId);
        Assert.Contains("Action=AssumeRoleWithWebIdentity", lastBody, StringComparison.Ordinal);
        Assert.Contains("WebIdentityToken=oidc-token-one", lastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-THAT-MUST-NOT-APPEAR", first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION-THAT-MUST-NOT-APPEAR", first.ToString(), StringComparison.Ordinal);

        clock.Now = clock.Now.AddMinutes(54);
        await identity.GetAsync(T.Ct);
        Assert.Equal(1, calls);

        await File.WriteAllTextAsync(file, "oidc-token-two", T.Ct);
        clock.Now = clock.Now.AddMinutes(2);
        await identity.GetAsync(T.Ct);
        Assert.Equal(2, calls);
        Assert.Contains("WebIdentityToken=oidc-token-two", lastBody, StringComparison.Ordinal);
        File.Delete(file);
    }

    [Fact]
    public void a_static_key_is_refused_for_an_endpoint_that_is_not_loopback()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => S3ObjectStore.Create(new S3ObjectStoreOptions
        {
            Endpoint = new Uri("https://nas.example:9000"),
            Bucket = new S3Bucket("varve"),
            DevelopmentKey = new S3DevelopmentKey("AKIA", "SECRET-THAT-MUST-NOT-APPEAR"),
            Clock = TimeProvider.System,
        }));

        Assert.Contains("ADR 0137", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-THAT-MUST-NOT-APPEAR", refused.Message, StringComparison.Ordinal);
        Assert.Equal("development key AKIA", new S3DevelopmentKey("AKIA", "SECRET-THAT-MUST-NOT-APPEAR").ToString());
    }

    private sealed class DelegatingAnswer(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
    }
}
