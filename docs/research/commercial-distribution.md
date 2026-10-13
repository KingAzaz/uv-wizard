# UV Wizard: selling and funding a VRChat editor tool

Research date: 2026-10-12. Page access dates are also 2026-10-12 unless noted. **This is not legal advice.** Sections 4 to 6 describe licences and terms in general terms. Before relying on them, ask a lawyer, or ask VRChat directly.

**Scope change during research.** The brief began as "paid, closed-source NDMF plugin". Partway through, the developer chose **open source (public repo), selling convenience rather than code**. So this document covers these topics in depth:

- storefronts (section 1)
- delivery through VCC/VPM (section 2)
- monetization models for open-source tools (section 3, new)
- licensing (section 4)
- VRChat terms (section 5)
- the existing repo (section 6)

Closed-source protection is cut down to a short note (section 7).

**Labels** (same convention as `competitive-landscape.md`):

- **[F] Observed fact.** Read directly in a primary source: store pages, official docs, legal text, source code, or the GitHub API. A URL is given.
- **[I] Inference.** My interpretation. It may be wrong.
- **[R] Recommendation** for UV Wizard.
- **[U] Unverified.** I looked but could not confirm it from a primary source. Where a secondary source exists, it is named.

Pinned commits that were read: vrc-get `4a12206b023b`, Gumroad (antiwork/gumroad) `eec9378ba8b7`, PoiyomiToonShader `f275eefcc210`, VRCFury `6bd33f4cb453`, NDMF `e89e1f6f3610`, LightLimitChangerForMA `c97c7f7c7b28`. BOOTH prices and "like" counts come from each item's public `.json` endpoint (`https://booth.pm/ja/items/<id>.json`). Gumroad prices come from the product page's embedded data.

---

## 0. Summary

- **[I] Japanese VRChat tools are mostly on BOOTH, and most open-source ones are free there with an optional donation variant.** AAO, lilToon, GoGo Loco, VRCQuestTools and ALCOM all list a ¥0 variant next to a ¥450–¥500 "support" variant that contains the same thing (section 1.3). Western sellers use Gumroad and, more and more, Jinxxy and Payhip. Patreon is used for subscriptions (Poiyomi Pro). The Unity Asset Store and itch.io are minor channels for avatar tools.
- **[F] Paid closed tools exist and sell at small prices.** Polytool costs $25 on Gumroad, with a free trial that cannot save or upload. Light Limit Changer v2 costs ¥2,500 on BOOTH, and its v1 stays MIT on GitHub. A BOOTH atlas tool costs ¥2,500, and a BOOTH Quest material converter costs ¥1,000.
- **[F] VCC and ALCOM both support per-repository HTTP headers, which is how private VPM repos are reached.** No store offers first-party license-gated VPM. Paid tools that install through VCC use a public "installer" package that authenticates the buyer (Poiyomi Pro through Patreon; Elreen's Toolbox through a Jinxxy license). The usual fallback is a `.unitypackage` or zip downloaded from the store.
- **[I] For an open-source UV Wizard, the proven model is a free VCC listing plus a BOOTH/Gumroad/Jinxxy listing with a free and a supporter variant, plus GitHub Sponsors.** That costs nothing to set up and matches community norms. A later "open core" step is also proven: an MIT core with a paid Pro package, as Poiyomi and Light Limit Changer do. Source-available licences work (VRCFury), but the tool can then no longer be called open source.
- **[F] The current repo has no LICENSE file.** Its README says MIT and links to a `LICENSE` that returns 404. The MIT sentence was added *after* all six GitHub releases. Making the repo private would unpublish the GitHub Pages VPM listing.
- **[F] VRChat's own terms are ambiguous.** The SDK Materials licence is "non-commercial", and the VRChat Distro licence lets VRChat package files be redistributed only "at no charge". No VRChat term was found that addresses selling third-party editor tools. **[R]** Never bundle VRChat SDK files. Keep the core free of VRChat references.

---

## 1. Storefronts

### 1.1 Comparison

| Store | Seller fees | Currency / reach | License keys | Update delivery | VAT / sales tax |
|---|---|---|---|---|---|
| **BOOTH** (pixiv) | [F] Service fee **5.6% + ¥45** per order since 2025-10-28 13:00 JST. It was 5.6% + ¥22 before ([BOOTH announcement 832](https://booth.pm/announcements/832), [834](https://booth.pm/announcements/834)). | [F] Priced in JPY. Buyers can pay by PayPal, Visa/Mastercard/JCB/Amex, Rakuten Pay, bank transfer or convenience store ([customer guide](https://booth.pm/customer_guide)). [I] This is the main market for Japanese buyers. Western buyers can pay by card or PayPal. | [U] I found no native license-key feature. | [F] Buyers re-download from their purchase history at any time ([customer guide](https://booth.pm/customer_guide)). The "Library" lists all purchased downloads ([announcement 482](https://booth.pm/announcements/482), 2020-10-15). [U] Whether past buyers are notified of updated files was not confirmed. | [U] Not confirmed. BOOTH's help centre returned 403 to automated fetches. |
| **Gumroad** | [F] **10% + $0.50** on direct sales, **30%** on Discover marketplace sales, no monthly fee ([pricing](https://gumroad.com/pricing)). | [F] Defaults to USD. Payouts by direct deposit or PayPal, depending on country ([pricing](https://gumroad.com/pricing)). | [F] Yes. The API has `POST /v2/licenses/verify`, plus `enable`, `disable`, `decrement_uses_count` and `rotate` ([routes.rb](https://github.com/antiwork/gumroad/blob/eec9378ba8b7/config/routes.rb#L68-L83)). [U] A third-party mirror of Gumroad's docs says verify needs only `product_id` + `license_key` and no access token ([mirror](https://www.mintlify.com/antiwork/gumroad/api/licenses/verify)). The official docs page renders client-side and could not be read. | [U] Not verified from a primary page. | [F] Gumroad has been merchant of record since 2025-01-01 and says it "handles ALL your tax obligations", collecting tax "in regions where we have tax obligations" ([pricing](https://gumroad.com/pricing)). |
| **Jinxxy** | [F] **5%** platform fee (Standard tier) or **10%** (Supporter tier, more visibility), plus about 3% payment processing, plus **$1.50 per payout**. Payouts are weekly ([selling](https://jinxxy.com/about/selling)). | [F] USD shown. Processing covers "100+ countries" ([selling](https://jinxxy.com/about/selling)). | [F] It advertises "built-in licensing options and custom terms" ([selling](https://jinxxy.com/about/selling)). [F] Each purchase issues a license key that a creator API (`api.creators.jinxxy.com/v1/licenses`, `x-api-key` header) can look up. A third-party Discord bot uses this to verify keys ([zkxs/jinx](https://github.com/zkxs/jinx)). [I] The creator API key is secret, so verification inside a buyer's Unity editor needs a server you run. | [F] "Push updates and buyers automatically get an email notification with the latest version" ([selling](https://jinxxy.com/about/selling)). | [F] Merchant of record. Handles sales tax and VAT ([selling](https://jinxxy.com/about/selling)). |
| **Payhip** | [F] Free plan: 5% transaction fee. Plus ($29/mo): 2%. Pro ($99/mo): 0%. PayPal or Stripe fees apply on top ([pricing](https://payhip.com/pricing)). | [I] Global, USD-centric. | [F] Yes. Keys are unique per sale or uploaded. Verification uses `GET https://payhip.com/api/v2/license/verify` with a per-product `product-secret-key` header. Keys auto-disable on refund ([help 317](https://help.payhip.com/article/317-software-license-keys-new)). | [U] Not checked. | [F] It "collect[s] and remit[s] EU VAT and UK" VAT automatically ([pricing](https://payhip.com/pricing)). |
| **Patreon** | [F] **10%** platform fee for creators whose page goes live after 2025-08-04. Older pages keep legacy plans. Payment processing is extra, for example 2.9% + $0.30 on USD cards ([support article](https://support.patreon.com/hc/en-us/articles/36426991446797), [pricing](https://www.patreon.com/pricing)). | [I] Global. Subscription model. | [F] None built in. Poiyomi uses Patreon login to unlock VCC installs (section 2.3). | [I] Through posts and attachments, or your own Patreon-OAuth integration. | [F] Patreon handles "most applicable tax collections and remittance" ([pricing](https://www.patreon.com/pricing)). |
| **Unity Asset Store** | [F] The provider gets **70%** of the sale price ([Provider Agreement §4.3](https://unity.com/legal/as-provider), last updated 2026-04-13). | [I] Global. Delivered through Unity's package manager, not VPM. | [U] No store-level key system found. | [I] Through Unity's "My Assets". | [F] The provider is responsible for taxes. Unity "may" collect on the provider's behalf (§4.4). [F] §5.5.1 prohibits assets whose primary purpose is distributing assets outside the store. |
| **itch.io** | [F] "Open revenue sharing": the seller picks itch.io's share from 0–100%, 10% by default. Processing is about 2.9% + $0.30 ([docs](https://itch.io/docs/creators/payments)). | [F] Payouts by PayPal or Payoneer, $5 minimum, after a review that usually takes 10–14 days ([docs](https://itch.io/docs/creators/payments)). | [U] Not checked. | [I] Through the download page. | [F] In "collected by itch.io" mode, itch.io is merchant of record and always collects VAT. In "direct" mode the seller is responsible ([docs](https://itch.io/docs/creators/payments)). |
| **Ko-fi** | [U] Secondary sources say 0% on one-off tips and 5% on shop sales, memberships and commissions on the free plan, and 0% with Gold. Ko-fi's own pages returned 403. | | | | |
| **GitHub Sponsors** | [F] **No fee** on sponsorships from personal accounts. Up to 6% on sponsorships from organisations ([GitHub docs](https://docs.github.com/en/sponsors/getting-started-with-github-sponsors/about-github-sponsors)). | | n/a | n/a | |

### 1.2 Which storefronts paid VRChat tools mostly use

- **[F]** Japanese-made VRChat tools nearly all have a BOOTH page, including the free MIT ones (section 1.3).
- **[F]** Western examples: Polytool is on Gumroad. GoGo Loco is on both Gumroad and BOOTH. YGDR Animator Editor lists Gumroad, Jinxxy and BOOTH side by side ([README](https://github.com/YerGodDamnRight/YGDR-Animator-Editor)). Elreen's Toolbox is gated by a Jinxxy license ([Elreen/vpm](https://github.com/Elreen/vpm)).
- **[U]** Several Gumroad storefronts in search results say they are "moving to Jinxxy" or Payhip. I did not open and verify these one by one, so treat the migration as anecdotal.
- **[I]** BOOTH reaches Japanese buyers, who are a large part of the NDMF/AAO/TTT audience. Gumroad, Jinxxy and Payhip reach Western buyers, and all three act as merchant of record for VAT. [U] Whether BOOTH does the same is unconfirmed.

### 1.3 Examples with prices (checked 2026-10-12)

| Product | Where / price | Model | Licence / notes |
|---|---|---|---|
| **Polytool** (Markcreator) | [F] Gumroad. "Personal Use Version" **$25**. "Trial Version" **$0**, which "cannot be used to upload or save the optimized avatars". 454 ratings, average 4.7. Latest release v1.9.3 ([page](https://markcreator.gumroad.com/l/polytool)). | Paid closed tool with a crippled trial. | [F] Personal-use licence only. "You cannot take (free or paid) commissions using this package." Installed by importing a Unity package ([page](https://markcreator.gumroad.com/l/polytool)). [U] Whether it ships DLLs or source. |
| **Poiyomi Pro** | [F] Patreon, **$10/month tier or higher**. Installed through the VCC package "Poiyomi Pro" after Patreon login. Updates need an active subscription ([README L78-L90](https://github.com/poiyomi/PoiyomiToonShader/blob/f275eefcc210/README.md), [download docs](https://www.poiyomi.com/download)). | Open core plus a subscription. | [F] Free Poiyomi Toon is MIT. "Active development happens on the Pro shader in a private repository, with free releases happening periodically" ([README](https://github.com/poiyomi/PoiyomiToonShader/blob/f275eefcc210/README.md)). [F] About 45,949 patrons (third-party Patreon badge endpoint used in the README, 2026-10-12). |
| **Light Limit Changer v2** (Azukimochi) | [F] BOOTH **¥2,500**. The v1 free variant is marked "【更新・サポート終了】" (no longer updated or supported). 27,732 likes ([item 4864776](https://booth.pm/ja/items/4864776)). | Free MIT v1 plus paid v2 ("v2.0.0からは有償化", paid from v2.0.0) ([docs intro](https://github.com/Azukimochi/LLC-Docs/blob/main/docs/intro.md)). | [F] v1 is MIT on GitHub and distributed through a VCC repo. Last release 1.14.4, 2025-03-16 ([repo](https://github.com/Azukimochi/LightLimitChangerForMA)). |
| BOOTH atlas tool by 遊び屋Books, whose title translates as "a tool that atlases textures inside Unity, plus a tool that merges meshes" | [F] Atlas tool 2.5.1: **¥2,500**. Mesh merger 2.2.4: **¥2,500**. Legacy versions: ¥1,000 each. Published 2019 ([item 1430024](https://booth.pm/ja/items/1430024)). | Paid tool. | Direct competitor category. |
| Quest real-material converter by はむ屋 (【VRChat対応】Questリアルマテリアル変換ツール) | [F] BOOTH **¥1,000**, 2025 ([item 6760882](https://booth.pm/ja/items/6760882)). | Paid tool. | |
| **GoGo Loco** | [F] Gumroad **$0+, suggested $6.99**, 439 ratings ([page](https://franadavrc.gumroad.com/l/gogoloco)). BOOTH ¥0 plus a "Support Me !" variant at **¥450**, 22,302 likes ([item 3290806](https://booth.pm/ja/items/3290806)). | Pay what you want. | The GitHub repo has no licence detected ([Franada/goloco](https://github.com/Franada/goloco)). |
| **AAO: Avatar Optimizer** | [F] BOOTH: free variant **¥0** plus "支援版(同一)" (supporter edition, identical contents) at **¥500**. 31,568 likes ([item 4885109](https://booth.pm/ja/items/4885109)). | Free plus donation variant, plus GitHub Sponsors (23 public sponsors). | MIT. |
| **lilToon** | [F] BOOTH ¥0 plus "投げ銭 (Donation)" **¥500**. 51,314 likes ([item 3087170](https://booth.pm/ja/items/3087170)). | Free plus donation variant. | MIT. |
| **VRCQuestTools** | [F] BOOTH ¥0 plus a donation variant at ¥500 ([item 2436054](https://booth.pm/ja/items/2436054)). FUNDING.yml lists GitHub Sponsors, BOOTH and pixivFANBOX ([FUNDING.yml](https://github.com/kurotu/VRCQuestTools/blob/master/.github/FUNDING.yml)). | Free plus donation. | MIT. |
| **ALCOM** (vrc-get GUI) | [F] BOOTH: a free "link file" variant plus a ¥500 "支援版(同一)" (supporter edition, identical) ([item 6448396](https://booth.pm/ja/items/6448396)). | Free plus donation. | MIT. |
| **YGDR Animator Editor** | [F] Gumroad **$0+, suggested $10**. Also on Jinxxy and BOOTH, and installable from a VPM repo ([README](https://github.com/YerGodDamnRight/YGDR-Animator-Editor)). | Pay what you want on a copyleft tool. | GPL-3.0. Small project with 8 stars. |
| **VRLabs** | [U] No paid VRLabs items found. The org describes itself as making free tools, and its site links to Patreon ([vrlabs.dev](https://vrlabs.dev)). | Patreon donations. | MIT (for example Avatars 3.0 Manager). |
| **VRCFury** | [F] Free. | Source-available licence (section 3.6). | Custom licence. |

Note: BOOTH "likes" (`wish_lists_count`) are bookmarks, not sales. **[U]** No storefront publishes sales counts, so revenue cannot be compared.

---

## 2. Paid delivery through VCC/VPM

### 2.1 What VCC and ALCOM support

- **[F] VCC lets you add custom request headers per repository.** The docs say "if you want to access a private repository, you may need to provide an authorization token". You open the cogwheel next to the URL field in the add-repository dialog ([VCC community repositories](https://vcc.docs.vrchat.com/guides/community-repositories)).
- **[F] The listing format has no auth fields.** A listing is JSON with packages and versions, and each version's `url` is "a direct-download link to a zip file" ([repos](https://vcc.docs.vrchat.com/vpm/repos), [packages](https://vcc.docs.vrchat.com/vpm/packages/)). The docs "highly recommend" an SPDX `license` field.
- **[F] vrc-get CLI:** `vrc-get repo add <url> -H Name:Value` ([commands.rs](https://github.com/vrc-get/vrc-get/blob/4a12206b023b/vrc-get/src/commands.rs#L1048-L1061)).
- **[F] ALCOM GUI:** the add-repository command accepts a `headers` map ([bindings.ts](https://github.com/vrc-get/vrc-get/blob/4a12206b023b/vrc-get-gui/lib/bindings.ts)).
- **[F] ALCOM deep links:** they parse `vcc://vpm/addRepo?url=…&headers[]=Name:Value` ([deep_link_support.rs](https://github.com/vrc-get/vrc-get/blob/4a12206b023b/vrc-get-gui/src/deep_link_support.rs), [format doc](https://github.com/vrc-get/vrc-get/blob/master/docs/repository-list-file-format.md)).
- **[F] vrc-get also supports a per-package `headers` field in the listing** (PR #718, merged 2024-05-17). The issue behind it notes that a matching field exists in VCC's own `VRCPackageManifest` type ([#705](https://github.com/vrc-get/vrc-get/issues/705), [#718](https://github.com/vrc-get/vrc-get/pull/718)).
- **[U]** VCC's docs do not say whether VCC's own `vcc://` deep link accepts `headers[]`, or whether VCC honours per-package `headers`.
- **[F] VRChat's "Curated Community Packages" are invite-only**, and "currently, all of the packages listed here are open source" ([curated](https://vcc.docs.vrchat.com/vpm/curated-community-packages/), [packages](https://vcc.docs.vrchat.com/vpm/packages/)). There is no VRChat-run directory of community repos, and the docs set no policy on paid packages.

### 2.2 Store-side integrations

- **Jinxxy:** [U] I found no first-party Jinxxy VCC/VPM integration. What exists is creator-built gating that uses Jinxxy keys (Elreen, section 2.3).
- **BOOTH:** [U] I found no official BOOTH API or VPM bridge. Third-party asset managers sign in with the buyer's BOOTH session:
  - "Booth Package Manager" (BOOTH item 8109868) appeared in search results described as using login cookies, but the page returned 404 when fetched.
  - MioVRC_AssetManager stores web logins for BOOTH, Gumroad and Jinxxy ([README](https://github.com/CokoIya/MioVRC_AssetManager)).
  - [I] Scraping a logged-in session is fragile and may conflict with BOOTH's terms. Don't build delivery on it.
- **Gumroad and Payhip:** [I] Their verify endpoints make a small license proxy easy to build. Payhip's needs a per-product secret, so it has to run server-side. Gumroad's reportedly needs no token.

### 2.3 Patterns used in practice

1. **Public installer package that authenticates and then fetches the real package.**
   - [F] Poiyomi Pro: a VCC package opens the browser, the buyer logs into Patreon on pro.poiyomi.com, then the script installs Pro. Updates repeat the authentication and need an active subscription ([docs](https://www.poiyomi.com/download)).
   - [F] Elreen's Toolbox: the public listing has one package, "Small public installer that verifies an Elreen Toolbox license and installs the protected editor package", gated by a Jinxxy license ([index.json](https://elreen.github.io/vpm/index.json), [repo](https://github.com/Elreen/vpm)).
2. **Private listing with per-buyer headers.** [I] The store issues a key, and the buyer adds the repo with an `Authorization` header or through a deep link. A small server checks the key against the store API and serves the listing and zips. VCC and ALCOM both support the client side (section 2.1). [U] I found no well-known VRChat tool that uses this.
3. **Plain download from the store (the common fallback).** [F] Polytool ("import the Polytool package") and Light Limit Changer v2 (BOOTH) are bought and downloaded. VCC is not involved for the paid part.
4. **Free VCC listing, with the store used only for payment or discovery.** [F] Used by AAO, lilToon, GoGo Loco, VRCQuestTools, YGDR Animator Editor and Light Limit Changer v1. **[I] This is the natural fit for an open-source UV Wizard.**

---

## 3. Monetization models for open-source VRChat/Unity tools

For each model: examples, evidence, how it fits MIT or copyleft, and whether this community accepts it.

### 3.1 Patreon or Ko-fi tiers with early-access or Pro builds

- **Examples:**
  - [F] Poiyomi: free MIT Toon, with Pro at Patreon $10+/month and about 45,949 patrons (section 1.3). This is the strongest evidence in the ecosystem that subscriptions work for a tool.
  - [F] z3y's shaders: MIT, with Patreon in FUNDING.yml ([FUNDING.yml](https://github.com/z3y/shaders/blob/main/.github/FUNDING.yml)).
- **Licence interaction:**
  - [I] Early access only "works" if the paid build is *not* under the open licence. Poiyomi Pro carries a EULA. An MIT or GPL build given to patrons can legally be passed on by them. MIT grants the right to "distribute, sublicense, and/or sell copies" ([MIT](https://opensource.org/license/mit)), and the GPL lets recipients redistribute ([FSF](https://www.gnu.org/philosophy/selling.html)).
  - [I] Under MIT, the honest form is "patrons get it N weeks earlier, then it goes public". That relies on goodwill, not law.
- **Acceptance:** [I] High. Poiyomi is the dominant avatar shader.
- **[U]** Patreon revenue figures are not public.

### 3.2 Pay what you want, or free plus a supporter variant on BOOTH/Gumroad, with a free VCC listing

- **Examples:**
  - [F] BOOTH: AAO (¥0 / ¥500), lilToon (¥0 / ¥500), GoGo Loco (¥0 / ¥450), VRCQuestTools (¥0 / ¥500), ALCOM (¥0 / ¥500).
  - [F] Gumroad: GoGo Loco ($0+, suggested $6.99), YGDR Animator Editor ($0+, suggested $10).
- **Licence interaction:** [F/I] Fully compatible with MIT and with GPL. The FSF explicitly encourages charging for copies ([FSF](https://www.gnu.org/philosophy/selling.html)).
- **Acceptance:** [I] This is *the* norm for Japanese NDMF-era tools. A store page also serves as discovery: BOOTH search is where Japanese users look.
- **Cost:** BOOTH takes 5.6% + ¥45 per paid order. [I] On a ¥500 donation that is about ¥73, roughly 15%.
- **[U]** No conversion or revenue data is published.

### 3.3 Open core: free MIT core plus a paid closed "Pro" package

- **Examples:**
  - [F] Poiyomi (Toon MIT / Pro paid).
  - [F] Light Limit Changer (v1 MIT and free; v2 ¥2,500, "paid from v2.0.0"; v1 support ended).
- **Licence interaction:**
  - [I] Works cleanly if the core is MIT. A closed Pro package can depend on it through `vpmDependencies`.
  - [F] GitHub's terms make outside contributions arrive under the repo's licence ("inbound=outbound") ([GitHub ToS D.6](https://docs.github.com/en/site-policy/github-terms/github-terms-of-service)). [I] With an MIT core, contributed code can legally be reused in the closed Pro package, but contributors may object socially.
  - [I] With a GPL core, a closed Pro assembly that links to it raises derivative-work questions, unless you own all the copyright (for example through a CLA).
- **Acceptance:** [I] Accepted when the free tier stays useful. LLC v2 has 27,732 BOOTH likes. [U] I found no documented backlash about LLC v1 being frozen, but I did not search Japanese social media.

### 3.4 Paid support and commissions

- **Evidence:** [U] Creators sell Quest optimization or conversion as a service. Search snippets show about $10 to $250 per avatar, for example on [payhip.com/b/muN9r](https://payhip.com/b/muN9r) and [ossvr.carrd.co](https://ossvr.carrd.co/). I did not open each page.
- **Licence interaction:**
  - [F] Polytool's licence forbids taking commissions with it ([page](https://markcreator.gumroad.com/l/polytool)).
  - [I] An MIT UV Wizard lets anyone, including you, sell "done-for-you" optimization. That makes it attractive to commission artists, which grows adoption. Paid priority support or a Discord role fits any licence.
- **Acceptance:** [I] Commissions are a normal part of the VRChat economy.

### 3.5 GitHub Sponsors

- **Evidence:**
  - [F] 0% fee on sponsorships from personal accounts ([docs](https://docs.github.com/en/sponsors/getting-started-with-github-sponsors/about-github-sponsors)).
  - [F] Public sponsor counts from GitHub GraphQL on 2026-10-12: bdunderscore (NDMF, Modular Avatar) **25**, anatawa12 (AAO, vrc-get) **23**, kurotu (VRCQuestTools) **1**. Amounts are not public.
- **Takeaway:** [I] Even the ecosystem's best-known authors have only a few dozen sponsors. Treat Sponsors as a supplement to a BOOTH/Gumroad page, not the main income.
- **Licence interaction:** compatible with any licence.

### 3.6 Dual or source-available licences

- **VRCFury** ([LICENSE.md](https://github.com/VRCFury/VRCFury/blob/6bd33f4cb453/LICENSE.md)):
  - [F] The licence offers three options: a Personal licence (use, modify and redistribute for personal purposes), a Commercial licence, and a VRCA/VRCW licence for the shader inside uploaded avatars and worlds.
  - [F] Under the Commercial licence, VRCFury must be downloaded by the end user from an official channel, must not be redistributed with the product, and must not be modified.
  - [F] It defines commercial purposes to include donations.
  - [F] The README states "VRCFury is source-available, not OSI open source".
  - [F] The FAQ gives the reason: to stop avatar sellers from shipping modified "exclusive" paid forks.
- **PolyForm Noncommercial 1.0.0:**
  - [F] It permits "any noncommercial purpose" and personal use "without any anticipated commercial application" ([PolyForm](https://polyformproject.org/licenses/noncommercial/1.0.0)).
- **BSL 1.1:**
  - [F] It allows non-production use, plus an optional "Additional Use Grant".
  - [F] Each version converts to a GPL-compatible "Change License" on its Change Date, which is at most 4 years after release ([BSL 1.1](https://mariadb.com/bsl11/)).
- **"Free for personal use, paid for commercial avatar sellers":**
  - [I] This is a dual licence, the same shape as VRCFury's.
  - [I] Enforcement is hard. A non-destructive NDMF plugin leaves no trace in the uploaded avatar, only meshes and textures.
  - [I] Sellers would tell their buyers to install UV Wizard themselves, which VRCFury's FAQ explicitly allows ("instruct your users to download VRCFury").
- **Licence interaction:**
  - [F] None of these is open source by the OSI definition. Criterion 6 says a licence "must not restrict anyone from making use of the program in a specific field of endeavor" ([OSD](https://opensource.org/osd)).
  - [I] You could no longer call UV Wizard "MIT" or "open source".
  - [I] Outside contributions would need a CLA so you can keep dual-licensing.
  - [I] MIT dependencies (NDMF, AAO) don't block any of these.
- **Acceptance:**
  - [I] VRCFury proves that users accept source-available tools when they are free for personal use.
  - [I] It conflicts with the developer's stated "open source" plan.
  - [F] It would also break the current "all open source" description of VCC's curated list ([curated](https://vcc.docs.vrchat.com/vpm/curated-community-packages/)).

### 3.7 What "selling convenience" can mean with an MIT tool

[I] Under MIT, anyone may resell the tool ([MIT](https://opensource.org/license/mit)). [U] I found no case of a free VRChat MIT tool being resold. Things buyers would pay for even though the code is free:

- a one-click BOOTH, Gumroad or Jinxxy listing that includes the VCC "Add to VCC" link
- Japanese and English documentation
- tested presets, such as a "Quest Good" target
- a supporter Discord role and priority bug fixing
- early access (section 3.1)
- a separate closed Pro package (section 3.3)

---

## 4. Licensing compatibility

- **Dependencies used without bundling them:**
  - [F] MIT's only condition is that the copyright and permission notice "shall be included in all copies or substantial portions of the Software" ([MIT](https://opensource.org/license/mit)).
  - [I] Declaring NDMF or AAO in `vpmDependencies` and letting VCC fetch them from their own repos means you distribute no copy, so no attribution is legally required. A "Dependencies / thanks" section in the README is still good manners.
  - [I] This holds whether UV Wizard is MIT, GPL or closed.
- **Logo and image exceptions:** [F] Modular Avatar and TexTransTool exclude their logo and image files from MIT. Those files may be redistributed only inside the official packages ([MA COPYING.md](https://github.com/bdunderscore/modular-avatar/blob/main/COPYING.md), [TTT LICENSE.md](https://github.com/ReinaS-64892/TexTransTool/blob/master/LICENSE.md)). Don't copy their icons.
- **If you copy MIT code:**
  - [F] Copying, modifying and selling are all permitted, provided the original notice is kept ([MIT](https://opensource.org/license/mit)).
  - [I] Keep TTT's or AAO's copyright line next to the copied code, or in a `THIRD_PARTY_NOTICES` file. AAO already does this for code it took from TTT (see `competitive-landscape.md`).
  - **[I] So the clean-room plan (read TTT/AAO, don't copy) is not legally needed for MIT sources.** It only matters for GPL code (Blender's packer, Material Combiner) if UV Wizard stays MIT.
- **Choosing UV Wizard's own licence:**
  - [F] MIT ("Expat") is GPL-compatible ([FSF list](https://www.gnu.org/licenses/license-list.html#Expat)).
  - [I] MIT keeps every option in section 3 open except source-available. It also matches NDMF, AAO, TTT, MA and lilToon.
  - [I] GPL-3.0 would also accept the MIT dependencies, but it makes a closed Pro add-on harder (section 3.3).
- **VRChat files:**
  - [F] The VRChat package in this project carries a "VRCHAT DISTRO LICENSE FILE" (version 2022-02-24). It lets the files be redistributed "on a non-commercial basis (i.e., at no charge)" (`Packages/com.vrchat.core.vpm-resolver/License.md`, referencing [hello.vrchat.com/legal/sdk](https://hello.vrchat.com/legal/sdk)).
  - **[R] Never include VRChat SDK files in a UV Wizard zip or unitypackage.** Declare them as dependencies instead.

---

## 5. VRChat terms

- **Terms of Service** (effective 2026-02-09) ([legal](https://hello.vrchat.com/legal)):
  - [F] Section 9 ("Creator Economy") governs selling content *on* the VRChat platform through VRChat-approved marketplaces. Section 9.2(h) covers one-time sales and purchaser rights.
  - [F] Section 12 forbids "any commercial uses" of the Platform without a separate written "Commercial Use Agreement".
  - [F] Section 17 forbids selling or distributing VRChat "Materials", which include SDKs, except as the terms allow.
  - [F] Section 5 makes SDK use subject to the SDK licence at [hello.vrchat.com/legal/sdk](https://hello.vrchat.com/legal/sdk).
  - [I] None of these clauses mentions selling third-party Unity editor tools off-platform.
- **SDK / VRChat Materials License Agreement** (effective 2021-11-05):
  - [F] §2.3 grants a "limited, personal, revocable, non-commercial" licence to use SDKs "solely in connection with Licensee's internal development of Content".
  - [F] §2.6 says no one may "use any VRChat Materials or Content for any commercial uses" except as the licences or a written agreement allow.
  - [F] §4.3(h) forbids using VRChat Materials "to develop code, software, or other materials for anything other than for use within the VRChat Platform".
  - [I] This reads strictly, yet a large, open market of paid avatars and paid tools exists on BOOTH and Gumroad. I found no VRChat statement that applies these clauses to third-party tool sellers.
  - [I] The risk is lowest when the product contains no VRChat Materials, and when its core does not need the SDK at all. The tech-stack plan's VRChat-free `Core.Editor` assembly helps here.
  - [I] A free, open-source UV Wizard with optional donations sits furthest from the "commercial use" question.
- **Creator Guidelines** (updated 2026-10-06): [F] Nothing about selling tools. The NFT section forbids promoting "unauthorized products or services" in that context ([guidelines](https://hello.vrchat.com/creator-guidelines)).
- **VCC:** [F] There is no published community-repo policy on paid packages (section 2.1). The curated list is invite-only.
- **[R]** If UV Wizard ever sells a closed Pro package at scale, ask VRChat in writing whether the SDK licence's "non-commercial" wording applies to editor add-ons sold off-platform.

---

## 6. The existing public repo (clnelson888/uv-wizard)

### 6.1 Facts (GitHub API and local clone, 2026-10-12)

- **Visibility and licence:**
  - [F] Public. Created 2024-11-09. `licenseInfo: null`. The root contains no LICENSE file.
  - [F] README line 327 says: "This project is licensed under the [MIT License](LICENSE). You are free to use, modify, and distribute this software, but please provide attribution to the original author." The `LICENSE` link returns HTTP 404.
  - [F] There is no copyright line anywhere. `UVWizard.cs` has no header, and `package.json` has no `license` field.
- **Releases:**
  - [F] **Six** releases, not seven: 0.0.6, 0.0.7, 0.0.8, 0.0.9.1, 0.1.0 and 0.1.1. They were published between 2024-11-09 21:53 UTC and 2024-11-10 00:58 UTC.
  - [F] Total asset downloads: 414, of which 343 were 0.0.9.1.
  - [F] The 0.1.1 zip contains only `package.json`, `Editor/UVWizard.cs`, an asmdef and `.meta` files. It has no licence text.
- **When the MIT sentence appeared:** [F] It was added in commit `13d6e75` at 2024-11-10 04:31 UTC, *after* the last release.
- **VPM listing:** [F] A VPM listing is live on GitHub Pages at `https://clnelson888.github.io/uv-wizard/index.json`. A `build-listing.yml` workflow builds it.
- **Contributors:** [F] One contributor (clnelson888, 18 commits). 0 forks, 1 star, 0 pull requests.

### 6.2 General position (not legal advice)

- **Without a licence:**
  - [F] Without a licence, "default copyright laws apply" ([GitHub docs](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)), and others "have no permission… to use, modify, or share" ([choosealicense](https://choosealicense.com/no-permission/)).
  - [F] Publishing a public repo grants GitHub users only a licence to view and fork it *through GitHub* ([ToS D.5](https://docs.github.com/en/site-policy/github-terms/github-terms-of-service)).
- **Does a README-only MIT statement count?**
  - [I] Most likely yes, as a statement of licence intent. A clear "licensed under the MIT License" sentence by the sole copyright holder is generally read as granting MIT terms, even though the full text and copyright line are missing.
  - [I] The broken link and missing copyright line make it weaker and more ambiguous than a proper LICENSE file. [U] No court ruling on this exact situation was found.
  - [F] OSI advises including the licence text with the code ([OSI FAQ](https://opensource.org/faq)).
- **Copies downloaded before the README change:** [I] The 414 release downloads all happened, or at least started, before or around the README change, and the zips carry no licence. Whether those copies count as MIT-licensed is unclear. Practically it hardly matters: the prototype is a few hundred lines and will be replaced.
- **Relicensing or going private:**
  - [I] A grant that was already made generally can't be clawed back from people who received code under it. [F] OSI describes open source as giving "the perpetual right to fork covered code" ([OSI FAQ](https://opensource.org/faq)).
  - [I] As sole author, with no outside contributions, the developer *can* license future versions under any terms, including closed or dual.
- **Mechanical effects of making the repo private:**
  - [F] Forks stay public. [F] Stars and watchers are erased. [F] **Any published GitHub Pages site is automatically unpublished** ([GitHub docs](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility)).
  - [I] That would break the VPM listing for anyone who added it, and the release download URLs would stop working.

### 6.3 Recommended actions, given the open-source decision

- **[R]** Add a root `LICENSE` with the standard MIT text and a line such as `Copyright (c) 2024-2026 clnelson888`.
- **[R]** Add `"license": "MIT"` to `package.json`, as VCC recommends an SPDX `license` field.
- **[R]** Keep the repo public so the Pages listing stays up.

These steps remove the ambiguity and match what the README already promises.

---

## 7. Short note: protecting closed-source C# (now secondary)

- **Precompiled DLLs:**
  - [F] Unity's plug-in importer has three relevant settings ([Plugin Inspector](https://docs.unity3d.com/2022.3/Documentation/Manual/PluginInspector.html)):
    - "Validate References" reports missing or mismatched dependencies.
    - "Auto Reference" controls whether default assemblies see the DLL.
    - "Define Constraints" gates loading on scripting symbols.
  - [F] Version Defines are asmdef rules that define a symbol depending on installed package versions ([asmdef docs](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptCompilationAssemblyDefinitionFiles.html)).
  - [I] In a precompiled DLL those `#if` branches are fixed at your build time.
  - [I] A DLL compiled against one NDMF version can fail with missing-member errors if NDMF changes its API. NDMF ships often: 1.10.0 (2025-12) to 1.14.8 (2026-08), with 1.15.0-beta.1 now ([CHANGELOG](https://github.com/bdunderscore/ndmf/blob/e89e1f6f3610/CHANGELOG.md)). [U] NDMF makes no explicit binary-compatibility promise that I found.
- **Obfuscation:** [F] The free Asset Store "Obfuscator" (id 48919) is marked deprecated ([page](https://assetstore.unity.com/packages/tools/utilities/obfuscator-48919)). [U] "Obfuscator Pro" ($87.99) targets built player assemblies. Its publisher and its handling of editor-only DLLs were not confirmed.
- **What paid tools do:**
  - [F] Poiyomi and Elreen gate *delivery* with server-side checks (section 2.3).
  - [U] I could not confirm whether Polytool or the BOOTH paid tools ship DLLs or source.
- **[I] This matters only for a future closed Pro add-on.** It doesn't affect the open-source core.

---

## 8. Recommendations for UV Wizard (open source)

1. **[R] Fix the licence now** (section 6.3).
2. **[R] Launch:**
   - Keep the free VCC listing, as today.
   - Open a BOOTH item with a ¥0 variant and a ¥500–¥1,000 "支援版(同一)" (supporter edition, identical) variant. Write the page in Japanese and English.
   - Open a Gumroad or Jinxxy item at "$0+" with a suggested $5–10. Jinxxy's fee is lower: 5% plus about 3% processing, against Gumroad's 10% + $0.50.
   - Add `.github/FUNDING.yml` with GitHub Sponsors plus the store links.
   - Every page should carry the `vcc://vpm/addRepo?url=…` button.
3. **[R] Later, if demand appears:**
   - Option A: Patreon early access with a fixed public-release delay.
   - Option B: a separate closed "UV Wizard Pro" package, for example batch processing, presets or advanced packers. It would depend on the MIT core through `vpmDependencies` and be delivered by a small installer that checks a Gumroad, Payhip or Jinxxy key.
   - Don't move the free core to a source-available licence. It conflicts with the stated goal and with the "open source" positioning that VCC's curated list currently reflects.
4. **[R] Keep the core free of VRChat SDK references, and never bundle VRChat or NDMF files** (sections 4 and 5).

---

## 9. Could not verify / open questions

- BOOTH: VAT/consumption-tax handling, whether past buyers get update notices, payout options for overseas shop owners, and the BOOST (tip on top) feature. BOOTH's help centre returned 403. BOOST is known only from secondary sources.
- Ko-fi fees and Gold price. Ko-fi's own pages returned 403. Secondary sources conflict.
- Gumroad's official API page (renders client-side). The route is confirmed in the source code, and the no-token behaviour comes from a mirror.
- Whether VCC itself, as opposed to ALCOM, honours `headers[]` in deep links and per-package `headers`.
- Any first-party Jinxxy VCC integration, and any official BOOTH API.
- Sales or revenue for any tool. Only likes, ratings, patron counts and sponsor counts are public.
- Whether Polytool or the BOOTH paid tools ship DLLs.
- How VRChat interprets the SDK licence's "non-commercial" clauses for third-party editor tools.
- The user's brief says 7 releases. The GitHub API lists 6.
