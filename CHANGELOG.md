# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.26.0] - 2020-05-15
* Update package dependencies
* Updated minimum Unity Editor version to 2019.3.12f1 (84b23722532d)

## [0.25.0] - 2020-04-30
* Update package dependencies

## [0.24.0] - 2020-04-09
* Update package dependencies

## [0.23.0] - 2020-03-20
* Fixes in the runtime lit shader: metallic, smoothness and emission are now working as expected. The runtime and Editor appearance are getting closer.

## [0.22.0] - 2020-02-05

* Update package dependencies
* Fix crash on Mac il2cpp builds during Metal/OpenGL initialization.
* Add new tiny rendering settings build setting component
* `TinyDisplayInfo` is now exported via a configuration system
* Add custom inspectors for new light components
* Fix srgb color conversion to better match the editor scene view

## [0.21.0] - 2020-01-21

* Add support for cascade shadow maps (1 csm directional light, fixed to four cascades). Refer to the CascadeShadowmappedLight component for more information.
* Add support for spot light inner angle.
* Fix culling under non-uniform scale when CompositeScale is used
* Update package dependencies

## [0.20.0] - 2019-12-10

* Update the package to use Unity '2019.3.0f1' or later
* This is the first release of Project Tiny lightweight rendering package.
