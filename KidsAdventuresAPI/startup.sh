#!/usr/bin/env bash
#
# App Service (Linux) startup command for the API — the FALLBACK path.
#
# The API ships as a container now (KidsAdventuresAPI/Dockerfile), and the image carries
# Ghostscript and Poppler, so nothing here runs in the normal case. This is kept for one
# situation: putting the app back on code deploy, where the blessed .NET image has neither tool
# and printing would silently stop again. If that path is ever retired for good, delete this file
# and its Content entry in the csproj.
#
# It exists for one reason: RENDER_VALIDATION needs Ghostscript and Poppler on the host, and the
# blessed .NET image does not carry them. Without both, every printed book comes out
# NOT_RELEASABLE with "'gs' is not installed on this deployment" - the gate is deliberate ("a
# check that did not run is not a check that passed"), so the answer is to install the tools
# rather than to soften the gate. See docs/CONFIGURATION.md section 2.
#
# Why at startup and not once, by hand: the container is rebuilt from the image on every restart,
# deploy and scale-out, and only /home survives it. Anything installed into the system prefix is
# gone by the next cold start, so it has to be reinstalled by whatever starts the app. The one way
# to make it genuinely permanent is a custom image that already contains the two tools.
#
# The packages go into the system prefix rather than into /home. Ghostscript wants its resource
# directory and Poppler wants fontconfig; unpacking .debs into a private prefix means owning
# LD_LIBRARY_PATH and /etc/fonts by hand, which is a lot of fragile work to save a few seconds.
# apt puts every one of those where the binaries already look, which is also why no
# Beki__PrintPrep__*Path setting is needed: gs, pdftoppm and pdffonts end up on PATH.
#
# The app starts either way. A deployment that cannot reach the Debian mirrors is a deployment
# that serves books and refuses to release them for print - the same state as today, and much
# better than an API that will not come up at all.
set -u

APP_DLL="${APP_DLL:-/home/site/wwwroot/KidsAdventuresAPI.dll}"
CACHE_DIR="/home/data/apt-cache"
PACKAGES="ghostscript poppler-utils libfontconfig1"

log() { echo "[startup] $*"; }

if command -v gs >/dev/null 2>&1 \
  && command -v pdftoppm >/dev/null 2>&1 \
  && command -v pdffonts >/dev/null 2>&1; then
  log "Ghostscript and Poppler already present; skipping install."
else
  # partial/ as well as the directory itself: apt writes part-downloads there and refuses to
  # start with "Archives directory .../partial is missing" when it does not exist. Creating only
  # the parent leaves the very first cold start — the one with nothing cached — unable to install
  # anything, which is precisely the run this cache was added to serve.
  mkdir -p "$CACHE_DIR/partial"
  export DEBIAN_FRONTEND=noninteractive
  installed=0

  # The cache first, and the network only if the cache is not there yet.
  #
  # This is the difference between "it usually works" and "it stops depending on the internet".
  # apt-get update needs a mirror; dpkg -i does not. Once one cold start has filled /home with the
  # .debs and their dependencies, every later start installs from that copy, so a Debian mirror
  # that is down, blocked or slow at three in the morning can no longer leave this deployment
  # unable to release a printed book. The first boot after a cache wipe is still exposed; there is
  # no way to close that window short of baking the tools into the image.
  if compgen -G "$CACHE_DIR/*.deb" >/dev/null 2>&1; then
    log "Installing print-validation tools from the cache in $CACHE_DIR..."
    if dpkg -i "$CACHE_DIR"/*.deb >/dev/null 2>&1; then
      installed=1
    else
      log "Cached packages did not install cleanly; falling back to the network."
    fi
  fi

  if [ "$installed" -eq 0 ]; then
    log "Installing print-validation tools from the network ($PACKAGES)..."
    # The cache directory plus Keep-Downloaded-Packages is what fills /home for next time,
    # dependencies included, so the branch above can take over from here on.
    if apt-get update -qq \
      && apt-get install -y -qq --no-install-recommends \
        -o dir::cache::archives="$CACHE_DIR" \
        -o APT::Keep-Downloaded-Packages=true $PACKAGES; then
      installed=1
    fi
  fi

  # Asked of the tools themselves rather than of the installer's exit code: a package manager
  # that reported success while leaving no gs on PATH is exactly the failure this check exists
  # to catch, and it is the tools the release gate will look for.
  if [ "$installed" -eq 1 ] \
    && command -v gs >/dev/null 2>&1 \
    && command -v pdftoppm >/dev/null 2>&1 \
    && command -v pdffonts >/dev/null 2>&1; then
    log "Installed: $(gs --version 2>/dev/null) / $(pdftoppm -v 2>&1 | head -1)"
  else
    # Loud, and not fatal. The books still generate and still reach the families who bought
    # them; what stays blocked is the handover to the printer, which is what the log has to say
    # plainly so nobody spends a morning looking for it in the application code.
    log "WARNING: could not install $PACKAGES."
    log "WARNING: RENDER_VALIDATION will report 'skipped' and printed books stay NOT_RELEASABLE."
  fi
fi

log "Starting the API."
exec dotnet "$APP_DLL"
