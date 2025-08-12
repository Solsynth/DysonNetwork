# The Dyson Network

The Dyson Network is the backend of the Solar Network.
We’ve open-sourced it here to ensure full transparency and accessibility for everyone.

However, it is not designed for self-hosted due to several limitations:

1. Embedded Branding: Variables, classes, and function names are explicitly tied to the Solar Network. You likely wouldn’t want Solar Network branding appearing on your own instance.
2. Hardcoded URLs: Certain services rely on Solsynth LLC’s infrastructure, with URLs hardcoded directly into the code. This means your instance must remain connected to our services and the internet.
3. No documentation: We do not provide documentation for self-hosting or local deployment.
4. No support: We offer no support for self-hosted deployments.

That said, self-hosting remains technically possible if you choose to proceed.

Please note that according to the APGL v3 license,
if you host a modified version of the software,
you must open-source it under the same license.

## Documentation

While we don’t support self-hosting, we encourage developers to build applications on this foundation.

Check out the OpenAPI Documentation at `/swagger` path on any instance.
Or visit our official instance: [Solar Network](https://solian.app/swagger).

## License

The source code is under the APGL v3 license.
