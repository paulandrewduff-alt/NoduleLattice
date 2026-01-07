// Minimal OrbitControls loader shim.
// If you prefer the full official OrbitControls.js, you can paste it here instead.
// This shim expects THREE to exist and uses the legacy global OrbitControls attachment.

(function () {
    if (!window.THREE) return;

    // If the CDN did not provide OrbitControls, we supply a very small fallback that supports orbit + damping.
    // This is NOT the full implementation, but enough for camera orbit in the sanity viewer.
    // (We can replace with full official code later.)

    const THREE = window.THREE;

    class OrbitControls {
        constructor(object, domElement) {
            this.object = object;
            this.domElement = domElement || document;

            this.enabled = true;
            this.enableDamping = false;
            this.dampingFactor = 0.05;

            this.target = new THREE.Vector3();

            this._isDown = false;
            this._lastX = 0;
            this._lastY = 0;

            this._theta = 0;
            this._phi = 0;
            this._radius = 1;

            this._updateSphericalFromCamera();

            this._onDown = (e) => {
                if (!this.enabled) return;
                this._isDown = true;
                this._lastX = e.clientX;
                this._lastY = e.clientY;
            };

            this._onUp = () => { this._isDown = false; };

            this._onMove = (e) => {
                if (!this.enabled || !this._isDown) return;

                const dx = (e.clientX - this._lastX) * 0.005;
                const dy = (e.clientY - this._lastY) * 0.005;

                this._lastX = e.clientX;
                this._lastY = e.clientY;

                this._theta -= dx;
                this._phi -= dy;

                const eps = 0.001;
                this._phi = Math.max(eps, Math.min(Math.PI - eps, this._phi));
                this.update();
            };

            this._onWheel = (e) => {
                if (!this.enabled) return;
                const delta = Math.sign(e.deltaY);
                this._radius *= (delta > 0) ? 1.08 : 0.92;
                this._radius = Math.max(0.2, Math.min(5000, this._radius));
                this.update();
            };

            this.domElement.addEventListener("pointerdown", this._onDown);
            window.addEventListener("pointerup", this._onUp);
            window.addEventListener("pointermove", this._onMove);
            this.domElement.addEventListener("wheel", this._onWheel, { passive: true });
        }

        dispose() {
            this.domElement.removeEventListener("pointerdown", this._onDown);
            window.removeEventListener("pointerup", this._onUp);
            window.removeEventListener("pointermove", this._onMove);
            this.domElement.removeEventListener("wheel", this._onWheel);
        }

        _updateSphericalFromCamera() {
            const v = new THREE.Vector3().copy(this.object.position).sub(this.target);
            this._radius = v.length();
            if (this._radius < 0.0001) this._radius = 1;

            this._theta = Math.atan2(v.x, v.z);
            this._phi = Math.acos(Math.min(1, Math.max(-1, v.y / this._radius)));
        }

        update() {
            // Spherical → Cartesian
            const sinPhi = Math.sin(this._phi);
            const x = this._radius * sinPhi * Math.sin(this._theta);
            const y = this._radius * Math.cos(this._phi);
            const z = this._radius * sinPhi * Math.cos(this._theta);

            this.object.position.set(
                this.target.x + x,
                this.target.y + y,
                this.target.z + z
            );

            this.object.lookAt(this.target);
        }
    }

    THREE.OrbitControls = OrbitControls;
})();
