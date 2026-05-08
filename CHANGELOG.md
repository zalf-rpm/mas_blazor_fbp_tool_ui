# Changelog

## [1.6.0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.5.0...v1.6.0) (2026-05-08)


### Features

* add proper start stop and cleanup logic when switching component services ([fb907c7](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/fb907c7fcc10292a218ecafcbe276eee320369ab))
* redesign link label make expandible and see buffer state and increase size ([db5b776](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/db5b7766b4cf5233d4e2b7cc3eafacaeafc04ed2))


### Bug Fixes

* prevent unresolvable failure state ([048152d](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/048152dbea6b285b557f505e6868c2a59b31a589))
* use less chars until shorting process name ([65a1546](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/65a154673171d4ede2701bfd735304117ff5860f))

## [1.5.0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.4.0...v1.5.0) (2026-05-05)


### Features

* add better search with debounce and instant ([24b4f45](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/24b4f45943682c7cccb08cc38148867194eb1e7c))
* add favicon ([b109b95](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/b109b95b0f7bb4ef0899c86855406ec54b66f961))
* change ports design ([c160078](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/c160078d0ad415d80eee9615711ced680813c9dd))
* **editor:** streamline link and flow interactions ([7afe88a](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/7afe88ab3c9e1fb289bafb34c3e78ee979e79d59))
* **flow:** fit flow from canvas double-click ([e00a57e](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/e00a57ed0e48d6a0db54c5563dece82c50ff5304))
* **flow:** make port position dynamic ([114b8a0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/114b8a08feb278bb8b4412ac155bb02095c40951))
* make it even more compact and enable side bar toggle button ([cec655e](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/cec655ed91866c08ba9a8f59621ef73f48689b6c))
* move badges into the handle for components from the list and reveal on hover ([137b170](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/137b170af3ca7715f2b84a72594f88fa6250a00b))
* **ports:** add array-aware link state ([84b1831](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/84b183159be196801ee2340fec1d92baa6f32057))
* refactor component lifecycle ([e82f175](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/e82f1750738e100ac5b35951b61364a27a96aee4))
* refactor the component list pannel to be more spce saving and clean. ([6f18aa6](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/6f18aa6a31063eafad1968b9f62a409e35f680dc))
* **runtime:** rework lifecycle and process cleanup ([93d3ee8](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/93d3ee8cce8b556d1966cc57a9e32181b0138002))
* **ui:** increase the accuracy of the displayed lifecycle state by adding an indicator ([2616f28](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/2616f28cd8147ab2d87ce2a4dd301a393773e6c3))
* **ui:** order input ports bottom-up ([362cb8e](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/362cb8ea7f3d1c51e54caa0a32136c9b2942593d))
* **ui:** redesign sturdy ref bookmarks list ([aeacd09](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/aeacd095e78023bfe5b2c3012e75440d5588ac38))
* **ui:** refresh port visuals ([86c97be](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/86c97bec10ee9091c341102803189dabc7e1a638))
* unify the gaps add scrollbar gutter and make minimap and execute button solid. Also make sure execute button requires at least one channel and component service connected ([c9f97e4](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/c9f97e436acd4dabee128c1dbd94c9f6cfd981f3))
* upgrade to mudblazor 9.4 ([ec27ef0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/ec27ef05ebb12caab9f3296e8f0ca499a8d04b32))
* use a bundled font instead of google fonts or roboto ([d62bd41](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/d62bd41f84db63c9267ed4e8d227ac7ea3054ae5))


### Bug Fixes

* **colors:** unify port and channel state colors ([478a072](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/478a072d10abecbd7b8f71f7b92812c1f8804b72))
* **flow:** preserve port anchors for FBP links ([061ec50](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/061ec50985ca874de795f7caccb22230d4d711b6))
* **flow:** preserve port labels when renaming ([028ddb5](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/028ddb536232168a543fa8c93425d191c3ffe827))
* **layout:** keep ports clear of component corners ([2d2287f](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/2d2287f0aa2fa501ffcc0f6ccb3bcbd330b81e1b))
* **layout:** remove horizonatal and vertical scrollbars by fixing layout also add consistant padding around editor ([b9e4661](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/b9e4661f38213f2e76847aec0fb505da3e858071))
* mermaid export not working ([f31bbc7](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/f31bbc7648f3aeb2e392c2336b8839d31bb46263))
* only call disconnect on the in port when the last writer is removed ([0f30d07](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/0f30d078e9b055014edfba258b4d519693001425))
* regression from mudblazor upgrade use simple file upload as new version broke the old file upload in that use case ([fed7e0b](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/fed7e0b9248cada5db77282c53952b1c7f832810))
* try to improve component menu even more ([e29fb08](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/e29fb08f8e577c1a1d8f77cf618f7c29070bd21c))
* **ui:** keep connected ports measurable ([5481467](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/5481467fae7774a51d631a889ed086884733a3ef))

## [1.4.0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.3.2...v1.4.0) (2026-04-17)


### Features

* add config for remote debugging attach into running container ([d09189c](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/d09189c1afca921a01884750aecc017086ea4aa2))


### Bug Fixes

* disable prerender due to exception being thrown on page load otherwise ([d5043a8](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/d5043a80267a4d3e913d46de9016d65596c83776))

## [1.3.2](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.3.1...v1.3.2) (2025-09-17)


### Bug Fixes

* bug and removed wrong shared state ([bce4077](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/bce407708c70fe04553d8b944272c6d3efade831))

## [1.3.1](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.3.0...v1.3.1) (2025-09-17)


### Bug Fixes

* explicitly add dockerhub token and dont run the workflow on forks ([#5](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/issues/5)) ([942d2ca](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/942d2ca066f931867a13c3eb9c3dd10d71e6d7a4))

## [1.3.0](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/compare/v1.2.1...v1.3.0) (2025-09-16)


### Features

* add manifest and config based release please ([ba28383](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/ba283830f5f4685787effd0bef9739ba6cd59989))
* allow changing number of lines in IIP content field ([296fd2b](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/296fd2b9dd825cf6d6188ef904adb67cefb8c0e3))
* **CI:** add dockerfile dockerignore and bakefile ([f222c11](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/f222c11c66fe52f5f10410c1a76bd6b9f95772a7))
* **CI:** add release workflow ([5eb5d0c](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/5eb5d0c1d33e66a1b72deb65c4f4ecfb14ddb509))
* update package version with release please ([ac213db](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/ac213db39cb70e83817288929e37e96d4e45ebe2))
* update release please to use github actions bot instead of my PAT ([9ba2c96](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/9ba2c964daa2e24d3b190ee45e856d38567e8334))


### Bug Fixes

* **CI:** add issue write permission ([fedd1e8](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/fedd1e83df9bd5b989649227d1c9f8626e517950))
* **common:** resolve arbitrary hostname ([8e6448e](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/8e6448e718bea613dbd3e2de1443f55d55814a2f))
* image name ([83c70bd](https://github.com/zalf-rpm/mas_blazor_fbp_tool_ui/commit/83c70bd57c8ac578b63c8e8b2b754e90aac51ed8))

## [1.2.1](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/compare/v1.2.0...v1.2.1) (2025-08-25)


### Bug Fixes

* **CI:** add issue write permission ([fedd1e8](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/fedd1e83df9bd5b989649227d1c9f8626e517950))
* **common:** resolve arbitrary hostname ([8e6448e](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/8e6448e718bea613dbd3e2de1443f55d55814a2f))

## [1.2.0](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/compare/v1.1.0...v1.2.0) (2025-08-21)


### Features

* update package version with release please ([ac213db](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/ac213db39cb70e83817288929e37e96d4e45ebe2))
* update release please to use github actions bot instead of my PAT ([9ba2c96](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/9ba2c964daa2e24d3b190ee45e856d38567e8334))

## [1.1.0](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/compare/v1.0.1...v1.1.0) (2025-08-14)


### Features

* add manifest and config based release please ([ba28383](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/ba283830f5f4685787effd0bef9739ba6cd59989))

## [1.0.1](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/compare/v1.0.0...v1.0.1) (2025-08-08)


### Bug Fixes

* image name ([83c70bd](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/83c70bd57c8ac578b63c8e8b2b754e90aac51ed8))

## 1.0.0 (2025-08-08)


### Features

* **CI:** add dockerfile dockerignore and bakefile ([f222c11](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/f222c11c66fe52f5f10410c1a76bd6b9f95772a7))
* **CI:** add release workflow ([5eb5d0c](https://github.com/DAKISpro/mas_blazor_fbp_tool_ui/commit/5eb5d0c1d33e66a1b72deb65c4f4ecfb14ddb509))
