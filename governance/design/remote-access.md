# Remote access — connecting clients to the Host

- **Status:** Living design doc (2026-06-09). Records the *how / what / cost* of
  reaching the Host from remote devices. The *why* (the decision to make transport
  a swappable concern) is ADR-shaped and noted in [Open decisions](#open-decisions);
  feature-map [`A8`](../plans/rebuild/01-feature-map.md) is the spec touch-point.
- **Audience:** anyone wiring up how a phone / laptop / browser talks to the Host
  running on the always-on PC.

## The model — transport is just a pipe

The architecture already decides the hard part for us. The **Host runs on the PC
where the project files and the `claude`/`codex` CLIs live**, and every client —
Web (Blazor WASM), Mobile (MAUI), PC — is a **thin view over the Host's HTTP/SSE
API** (`/catalog`, `/projects`, `/flows`, `/flows/{id}/events`, …). The Host is the
single source of truth; all clients share its state because they all read the same
Host.

That means **the transport is not the file layer and not the state layer — it is
only the network path** from a client to the Host. Viewing files, sending commands,
streaming a run: all of that already rides the Host API and is unchanged by which
pipe we pick. Files are never copied to the device — the Host reads them locally and
streams content the client renders (like viewing a file on a web UI, not cloning a
repo).

So choosing a transport is choosing how to give that one always-on Host a reachable,
secure address from off-network devices. Whatever we pick must supply four things —
the things the pipe is *for*:

| Need | Why |
|---|---|
| **NAT traversal** | the Host sits behind a home router (and later maybe a VM behind NAT/CGNAT) |
| **Encryption** | traffic crosses the public internet |
| **Stable addressing** | clients need an address that doesn't change |
| **Access control** | only *our* devices/users may reach the Host |

The plan ships this in **two phases**, and nothing built in Phase 1 is thrown away
in Phase 2.

---

## Phase 1 (now) — Tailscale mesh

A WireGuard **mesh VPN**. Each device joins a private network (a *tailnet*) and gets
a stable `100.x.y.z` address; the Host is reachable at its tailnet address. This is
the lowest-effort option and the one the MAUI client already assumes (it points at a
tailnet IP today).

### How it covers the four needs

- **NAT traversal:** Tailscale brokers a direct peer-to-peer connection through home
  NAT/CGNAT — no port forwarding, no router config.
- **Encryption:** WireGuard, device-to-device.
- **Stable addressing:** the `100.x.y.z` per device is fixed.
- **Access control:** the **mesh itself is the gate** — only enrolled devices can
  reach the Host. This is why feature-map `A8` can say *"No app-layer auth"*: the
  network is the auth. **No auth code ships in Phase 1.**

### What it costs

- **$0.** Tailscale's free *Personal* plan covers 6 users + ~100 devices, free
  forever. We are one user with a few of our own devices — permanently inside free.
- **No bandwidth billing is even possible.** The data plane is **direct
  peer-to-peer** — our bytes never traverse Tailscale's servers, so there is nothing
  for them to meter. Streaming heavy agent output / file content back and forth has
  **zero** cost impact. Cost scales only with *human users*, which is fixed at one.

### What you do

Install the client on each device once and log in. Adding a device later = install +
log in (your "add devices manually"). The client base address points at the tailnet
IP (the one-line swap the MAUI README already documents).

### Cost to your own internet

**None.** Tailscale is **split-tunnel by default** — only traffic addressed to the
Host's tailnet IP takes the private route; all normal browsing/streaming goes out
your regular connection untouched. Your PC is *not* "put on a VPN"; it gains one
extra private route to one machine. (Full-tunnel via an exit node is opt-in and we
won't use it.)

### Limits

- **Browser/web cannot join a tailnet** — a browser is not a mesh node. So in
  Phase 1 the **Web client is deferred**; "my own devices via apps" is the v1 target.
- Each client device needs the Tailscale client installed and enrolled.

---

## Phase 2 (when we want zero client footprint + web) — Cloudflare Tunnel + Access

A reverse tunnel that gives the Host a public `https://…` URL. A connector
(`cloudflared`) on the Host dials **out** to Cloudflare; clients just open the URL.
**Cloudflare replaces Tailscale for every client — it is "Cloudflare instead," not
"Tailscale + Cloudflare."**

### How it covers the four needs

- **NAT traversal:** the connector dials outbound; works behind NAT/CGNAT with no
  inbound ports.
- **Encryption + stable addressing:** free TLS on a fixed hostname.
- **Access control:** the mesh gate is gone (the door is now public), so this is the
  phase where **an auth layer becomes mandatory** — and Cloudflare *provides* it via
  **Access** (see below), so we still don't *build* one.

### What it unlocks

- **Web comes back** — any browser can open the URL. This is the headline advantage:
  web works *because it no longer needs a mesh*.
- **Zero client footprint** — no app, no VPN, nothing to install or run on the
  laptop/phone. Clients just make HTTPS calls.

### Auth without building an auth layer

Cloudflare **Access** sits in front of the open Host and validates the caller before
any request reaches us. We write a *policy*, not auth code. Three fits, cheapest
first:

| Mechanism | Shape | Best for | Notes |
|---|---|---|---|
| **Service token** | client sends `CF-Access-Client-Id` + `CF-Access-Client-Secret` headers; policy action **Service Auth** → no login UI | our **own apps** (MAUI desktop/mobile set their own headers) | "just an ID." Bearer secret — one token per device so you can revoke individually. Free. Valid ~1yr, then rotate. |
| **mTLS client cert** | per-device certificate Cloudflare validates transparently | apps / CLI wanting true device binding | Free with Cloudflare-managed CA (issuance cap); BYO-CA needs Enterprise. **Browser mTLS is flaky** — not for the web client. |
| **SSO / email-OTP login** | one Cloudflare-hosted login per device per session, long cookie | the **browser/web** client | the practical choice for browsers, since tokens/mTLS are awkward there. Still no code on our side. |

The Host stays auth-free; optionally it can verify the JWT Cloudflare injects
(`Cf-Access-Jwt-Assertion`) for defense-in-depth — a few lines, not a system.

### What it costs

- **$0 for us.** The tunnel is free with **no bandwidth charge, no throughput limit,
  no per-connector charge** — Cloudflare bills *per user*. Access is free up to **50
  users**; we are one.
- **Difference from Tailscale:** traffic *does* flow through Cloudflare (not P2P), so
  there is a soft **fair-use / ToS ceiling** aimed at large-scale media streaming.
  Our workload (code, agent text, command I/O, file content) is a rounding error and
  won't approach it. No per-GB billing exists on this path.

### Cost to your own internet

**None** — clients run no VPN at all; they just make HTTPS requests. There is nothing
to slow down on the client side.

---

## The "no turn on / turn off" guarantee

The whole point: **you never open a VPN to start working and close it after.** These
are not commercial full-tunnel VPNs (connect-to-use, disconnect-after). Here is how
each surface gets "always reachable, nothing to toggle":

| Surface | Phase 1 (Tailscale) | Phase 2 (Cloudflare) |
|---|---|---|
| **Corner PC (Host)** | Client runs as a **service that auto-starts on boot** and reconnects itself. Headless, monitor-less, zero interaction — on the mesh from power-on. | `cloudflared` runs as an **always-on service**, dials out on boot. Same: nothing to toggle. |
| **Desktop client (your PC)** | Same service model — runs in the tray, reconnects on boot/login. Set once. | **Nothing installed.** Just open the URL. |
| **Mobile** | Enable the VPN profile **once**; it persists in the background and auto-reconnects. *Not* per-session. (Caveat: after a phone reboot you may re-tap once.) | **Nothing installed.** Just open the URL. |
| **Browser / web** | N/A (a browser can't join the mesh) — deferred to Phase 2. | **Nothing installed.** Just open the URL. |

Why it's "free" to leave on: Phase 1 is **split-tunnel** (idle until you talk to the
Host, no effect on other traffic); Phase 2 has **no client tunnel at all**. So
leaving it "always on" costs nothing — there is no penalty that would make you want
to turn it off, which is exactly why you never have to turn it on.

The one thing we **cannot** do: have our app *programmatically* start the standalone
Tailscale VPN on iOS/Android — the OS sandbox forbids one app launching another's
VPN, and only one VPN runs at a time. That limitation is what Phase 2 sidesteps
entirely (no VPN to start). For Phase 1 it's a non-issue in practice because the
profile, once enabled, just stays up.

---

## Cost summary

| | Priced by | Our cost | Bandwidth risk |
|---|---|---|---|
| **Tailscale (Phase 1)** | Users (6 free) | $0, forever | **None** — traffic is P2P, unmeterable |
| **Cloudflare (Phase 2)** | Users (50 free) | $0, forever | **None** in practice; soft ToS ceiling only for extreme media |

Neither scales by bandwidth. The only lever that costs money on either is **number
of human users** — fixed at one. Cost is therefore **not** a factor in the
Phase-1-vs-Phase-2 choice; pick on the *sealed-mesh vs public-URL* trade-off, not on
price.

---

## What changes in our code

- **Phase 1:** essentially nothing — the client's base address points at the Host's
  tailnet IP (already true for MAUI). `A8`'s "no app-layer auth" holds.
- **Phase 2:** the client's base address points at the public hostname; our apps
  attach the Access service-token headers; optionally the Host verifies the injected
  Access JWT. The **auth concern is a separable middle layer** — it can be built any
  time, but going public is the event that makes it *required* (it replaces the
  network-level gate the mesh gave us for free). Do not flip to the public door
  before that layer exists.

The Host, Flows, file-viewing, and command paths are untouched across both phases.

---

## Staging & revisit triggers

- **Now:** Phase 1 (Tailscale), sealed, no auth. Covers "my own devices via apps."
- **Flip to Phase 2 when** any of: (a) we want true any-device **web**; (b) the
  install-and-enroll step per device becomes friction we want gone; (c) we want a
  shareable public URL. Phase 2 is **additive** — it can even run alongside the mesh
  during migration (Host reachable both ways) before the mesh is retired.
- **Revisit transport choice if:** we exceed Tailscale's free user/device caps
  (not foreseeable for personal use), or a future cloud VM already has a public IP
  (then a plain reverse proxy + TLS + Access may beat a tunnel), or "self-host the
  control plane too" becomes a requirement (then NetBird / Headscale / a VPS reverse
  tunnel replace the managed option — same architecture, more ops).

## Open decisions

- A thin **ADR** should record the *decision* ("transport is a swappable pipe;
  Tailscale now, Cloudflare-public later; auth is deferred and coupled to going
  public") and link here for the *how*. Not yet written.
- Feature-map **`A8`** still says *"Reachable over Tailscale."* It should be made
  transport-agnostic ("reachable over the device mesh / public tunnel") so the spec
  doesn't pin a vendor.

## Sources

- [Tailscale pricing](https://tailscale.com/pricing) · [pricing v4, 2026](https://tailscale.com/blog/pricing-v4)
- [Cloudflare Zero Trust plans](https://www.cloudflare.com/plans/zero-trust-services/)
- [Access service tokens](https://developers.cloudflare.com/cloudflare-one/access-controls/service-credentials/service-tokens/)
- [mTLS with Access](https://developers.cloudflare.com/cloudflare-one/access-controls/service-credentials/mutual-tls-authentication/)
</content>
</invoke>
