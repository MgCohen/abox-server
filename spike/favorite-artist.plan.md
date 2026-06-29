---
docType: feature-plan
status: approved
---

## Summary

Add a **Favorite-Artist collection** to the existing Spotify-clone app: a listener can
favorite any artist, see the favorites saved to a personal collection, view that
collection at any time, and unfavorite an artist. Done means the four behaviours work
end to end through the API and the favorites persist across sessions. This plan is the
**sample input** for the decomposition spike — it is a realistic plan, not production work.

## Context

The app already has working artist browsing, playback, and a per-user profile store. There
is no concept of a *favorite* — nothing lets a listener mark an artist and nothing persists
that mark. The gap is one new vertical slice (model + store + service + API) wired into the
existing user/profile and artist subsystems, following the app's established vertical-slice +
MediatR conventions.

## Scope

### In scope

- A `FavoriteArtist` model linking a user to an artist with a timestamp.
- Persistence for favorites (a store/repository over the existing data layer).
- A `FavoriteArtist` service exposing favorite / unfavorite / list-collection operations.
- API endpoints for favorite, unfavorite, and view-collection.
- DI registration and the docs for the new endpoints.

### Out of scope

- The client UI for the collection — owned by the Blazor client repo.
- Artist ingestion / the artist catalog itself — already built, reused as-is.
- Social features (sharing a collection, following) — a later plan.

## Decisions

### Vertical slice with MediatR, per app standards

The feature is built as one vertical slice with MediatR request handlers and thin
controllers, matching every other feature in the app. Why: consistency with the existing
codebase is worth more than a locally-cleaner shape, and the slice keeps the favorite
logic in one reviewable place.

### Favorites keyed by (userId, artistId)

A favorite is the pair plus a timestamp; favoriting is idempotent (re-favoriting is a
no-op, not a duplicate). Why: a listener can only favorite an artist once, and idempotency
keeps the API safe to retry.

## Phases

### Model the favorite

status: todo

Reuse the existing entity/base conventions; add a `FavoriteArtist` model (userId, artistId,
favoritedAt). **Goal.** A persisted shape exists for a favorite. **Done when.** The model
compiles and round-trips through the data layer.

### Stand up the store

status: todo

Reuse the app's repository pattern over the existing data context; add a favorites
store with add / remove / list-by-user. **Goal.** Favorites persist and are queryable per
user. **Done when.** Add → list returns it; remove → list omits it.

### Build the service

status: todo

Reuse the MediatR + vertical-slice convention; add a `FavoriteArtist` service with
favorite / unfavorite / list-collection over the store. **Goal.** The four behaviours exist
as service operations. **Done when.** Favorite is idempotent, unfavorite is safe on a
missing favorite, list returns the user's collection.

### Expose the API

status: todo

Reuse the existing thin-controller pattern; add favorite, unfavorite, and view-collection
endpoints over the service, then register everything in DI and document the endpoints.
**Goal.** The collection is reachable end to end over HTTP. **Done when.** The four
behaviours work through the API and the favorites survive a restart.

## Verification

- Build is warning-free and the app boots with the new slice registered.
- A behaviour run end to end: favorite two artists, list the collection (both present),
  unfavorite one, list again (one present), restart the app, list again (the survivor
  persists).
- Idempotency check: favoriting the same artist twice yields one collection entry.

## Open Questions

### Should unfavorite be a soft-delete or a hard-delete?

lean: hard-delete

A hard-delete is simplest and matches "the favorite is gone." A soft-delete would keep
history for future social/analytics features (out of scope now). Realistic options:
hard-delete now and revisit if a history feature is planned; or soft-delete pre-emptively.
Lean hard-delete — do not build for a feature that isn't planned.
