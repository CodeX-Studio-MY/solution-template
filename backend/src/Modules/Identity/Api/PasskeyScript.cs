namespace Farm360.Identity.Api;

/// <summary>The WebAuthn glue for the sign-in and security pages, served as a file so the CSP can stay <c>script-src 'self'</c>.</summary>
internal static class PasskeyScript
{
    public const string Source = """
        (() => {
          const button = document.getElementById('passkey');
          if (!button) return;
          const csrf = (document.querySelector('input[name=__csrf]') || {}).value;
          const tenant = new URLSearchParams(location.search).get('tenant');
          const post = (path, body) => fetch(`${path}?tenant=${encodeURIComponent(tenant)}`, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }, body: JSON.stringify(body) });
          button.addEventListener('click', async () => {
            const creating = button.dataset.mode === 'creation';
            const options = await (await post(`/account/passkey/${creating ? 'creation' : 'request'}-options`, {})).json();
            const publicKey = creating
              ? PublicKeyCredential.parseCreationOptionsFromJSON(options)
              : PublicKeyCredential.parseRequestOptionsFromJSON(options);
            const credential = creating
              ? await navigator.credentials.create({ publicKey })
              : await navigator.credentials.get({ publicKey });
            const result = await post(`/account/passkey/${creating ? 'register' : 'signin'}`, {
              credentialJson: JSON.stringify(credential), returnUrl: new URLSearchParams(location.search).get('returnUrl') });
            if (result.ok) { location.href = (await result.json()).redirect || '/'; }
            else { document.getElementById('passkey-error').textContent = 'The passkey was not accepted.'; }
          });
        })();
        """;
}
