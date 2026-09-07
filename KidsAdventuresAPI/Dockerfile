# The API image.
#
# It exists for one reason, and it is worth stating plainly: RENDER_VALIDATION needs Ghostscript
# and Poppler on the host, and App Service's blessed .NET image does not carry them. Installing
# them at startup works but cannot promise anything — the container is rebuilt from its image on
# every restart, deploy, scale-out and platform patch, so the install runs again each time and a
# Debian mirror that is unreachable at three in the morning is a printed book that silently
# cannot be released. Baked into the image, the tools are simply there, in every container the
# platform ever starts, with nothing to reach for at boot.
#
# Not a multi-stage build on purpose. The workflow already publishes the API — with
# SkipFrontendBuild so the csproj's npm/PowerShell target stays out of it, and with
# appsettings.Production.json stripped so App Service configuration is the only source of
# production values — and both of those matter. Restoring and publishing a second time inside
# Docker would duplicate that logic in a place where the next person would not think to look
# for it. So the build context IS the publish output, and this file only says what to put
# around it.

FROM mcr.microsoft.com/dotnet/aspnet:8.0

# ghostscript + poppler-utils: the two independent renderers the RENDER_VALIDATION gate names.
# libfontconfig1 and libfreetype6: what Poppler and SkiaSharp (QuestPDF's rasteriser) resolve
# fonts through. The apt lists are removed in the same layer so they are not carried in the image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ghostscript \
        poppler-utils \
        libfontconfig1 \
        libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

# Proof, at build time rather than at three in the morning. If a base image ever ships without
# one of these, or a package is renamed, the image fails to build and the deploy stops here —
# which is the whole point of moving the install out of the startup path.
RUN gs --version && pdftoppm -v && pdffonts -v

WORKDIR /app
COPY . .

# App Service probes the port it is told about. 8080 is what the .NET 8 images listen on by
# default; it is stated here as well so the container is not relying on a base-image default,
# and the app setting WEBSITES_PORT=8080 tells the platform the same number.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# CMD rather than ENTRYPOINT, deliberately.
#
# App Service has a "Startup command" field, and whatever is in it is handed to the container.
# Against an ENTRYPOINT it arrives as ARGUMENTS — an app already configured with
# "dotnet KidsAdventuresAPI.dll" would be started as
# "dotnet KidsAdventuresAPI.dll dotnet KidsAdventuresAPI.dll" and would not come up at all.
# Against a CMD it simply replaces it. So the image works whether that field is left empty (the
# tidy state, and what the docs ask for) or still carries the command from the code-deploy days.
CMD ["dotnet", "KidsAdventuresAPI.dll"]
