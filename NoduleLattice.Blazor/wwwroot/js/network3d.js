// Entry 013b/013c patch: Snapshot-only 3D rendering.
// Assumes THREE + OrbitControls are loaded globally via App.razor.

function requireThree() {
    if (!window.THREE) throw new Error("THREE is not loaded. Ensure App.razor includes three.min.js.");
    if (!window.THREE.OrbitControls) throw new Error("OrbitControls is not loaded. Ensure App.razor includes OrbitControls.js.");
    return window.THREE;
}

export function createNetwork3D(hostEl) {
    const THREE = requireThree();

    const scene = new THREE.Scene();

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    renderer.setSize(hostEl.clientWidth, hostEl.clientHeight);
    hostEl.appendChild(renderer.domElement);

    const camera = new THREE.PerspectiveCamera(60, hostEl.clientWidth / hostEl.clientHeight, 0.1, 5000);
    camera.position.set(8, 10, 14);

    const controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    const light1 = new THREE.DirectionalLight(0xffffff, 0.85);
    light1.position.set(10, 20, 10);
    scene.add(light1);

    const amb = new THREE.AmbientLight(0xffffff, 0.35);
    scene.add(amb);

    const state = {
        scene,
        renderer,
        camera,
        controls,
        nodes: null,
        edges: null,
        nodeGeom: null,
        edgeGeom: null,
        hostEl,
        _raf: 0,
        _onResize: null,
        _fitted: false
    };

    function animate() {
        state.controls.update();
        state.renderer.render(state.scene, state.camera);
        state._raf = requestAnimationFrame(animate);
    }
    state._raf = requestAnimationFrame(animate);

    state._onResize = () => {
        const w = state.hostEl.clientWidth;
        const h = state.hostEl.clientHeight;
        state.renderer.setSize(w, h);
        state.camera.aspect = w / h;
        state.camera.updateProjectionMatrix();
    };
    window.addEventListener("resize", state._onResize);

    return state;
}

export function updateNetwork3D(state, snapshot) {
    if (!state || !snapshot) return;

    const THREE = requireThree();

    // Support either camelCase or PascalCase from JSON serializers
    const nodes = snapshot.nodes ?? snapshot.Nodes ?? [];
    const synapses = snapshot.synapses ?? snapshot.Synapses ?? [];

    // Remove old meshes
    if (state.nodes) {
        state.scene.remove(state.nodes);
        state.nodeGeom.dispose();
        state.nodes.material.dispose();
        state.nodes = null;
    }
    if (state.edges) {
        state.scene.remove(state.edges);
        state.edgeGeom.dispose();
        state.edges.material.dispose();
        state.edges = null;
    }

    // Map id -> pos
    const posById = new Map();
    for (const n of nodes) {
        const id = n.id ?? n.Id;
        const pos = n.pos ?? n.Pos;
        posById.set(id, pos);
    }

    // Nodes
    const nodeCount = nodes.length;
    const positions = new Float32Array(nodeCount * 3);
    const colors = new Float32Array(nodeCount * 3);

    for (let i = 0; i < nodeCount; i++) {
        const n = nodes[i];

        const pos = n.pos ?? n.Pos;
        const x = pos.x ?? pos.X;
        const y = pos.y ?? pos.Y;
        const z = pos.z ?? pos.Z;

        positions[i * 3 + 0] = x;
        positions[i * 3 + 1] = y;
        positions[i * 3 + 2] = z;

        const vRaw = n.v ?? n.V ?? 0;
        const spiked = n.spiked ?? n.Spiked ?? false;

        const v = clamp((vRaw + 5.0) / 10.0, 0.0, 1.0);
        let r = v;
        let g = 0.25 + 0.35 * (1.0 - Math.abs(v - 0.5) * 2.0);
        let b = 1.0 - v;

        if (spiked) {
            r = clamp(r + 0.35, 0.0, 1.0);
            g = clamp(g + 0.35, 0.0, 1.0);
            b = clamp(b + 0.35, 0.0, 1.0);
        }

        colors[i * 3 + 0] = r;
        colors[i * 3 + 1] = g;
        colors[i * 3 + 2] = b;
    }

    const nodeGeom = new THREE.BufferGeometry();
    nodeGeom.setAttribute("position", new THREE.BufferAttribute(positions, 3));
    nodeGeom.setAttribute("color", new THREE.BufferAttribute(colors, 3));

    const nodeMat = new THREE.PointsMaterial({ size: 0.22, vertexColors: true });
    const nodePoints = new THREE.Points(nodeGeom, nodeMat);

    state.nodeGeom = nodeGeom;
    state.nodes = nodePoints;
    state.scene.add(nodePoints);

    // Edges
    const edgeCount = synapses.length;
    const epos = new Float32Array(edgeCount * 2 * 3);
    const ecolor = new Float32Array(edgeCount * 2 * 3);

    for (let i = 0; i < edgeCount; i++) {
        const e = synapses[i];

        const pre = e.pre ?? e.Pre;
        const post = e.post ?? e.Post;
        const wRaw = e.w ?? e.W ?? 0;
        const kind = e.kind ?? e.Kind ?? 0;

        const a = posById.get(pre);
        const bpos = posById.get(post);
        if (!a || !bpos) continue;

        const ax = a.x ?? a.X, ay = a.y ?? a.Y, az = a.z ?? a.Z;
        const bx = bpos.x ?? bpos.X, by = bpos.y ?? bpos.Y, bz = bpos.z ?? bpos.Z;

        const idx = i * 6;
        epos[idx + 0] = ax; epos[idx + 1] = ay; epos[idx + 2] = az;
        epos[idx + 3] = bx; epos[idx + 4] = by; epos[idx + 5] = bz;

        const w = Math.min(Math.abs(wRaw), 2.0);
        const bright = clamp(0.15 + (w / 2.0) * 0.85, 0.15, 1.0);

        let r = kind === 0 ? 1.0 : 0.25;
        let g = kind === 0 ? 0.65 : 0.45;
        let bb = kind === 0 ? 0.25 : 1.0;

        r *= bright; g *= bright; bb *= bright;

        ecolor[idx + 0] = r; ecolor[idx + 1] = g; ecolor[idx + 2] = bb;
        ecolor[idx + 3] = r; ecolor[idx + 4] = g; ecolor[idx + 5] = bb;
    }

    const edgeGeom = new THREE.BufferGeometry();
    edgeGeom.setAttribute("position", new THREE.BufferAttribute(epos, 3));
    edgeGeom.setAttribute("color", new THREE.BufferAttribute(ecolor, 3));

    const edgeMat = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0.65 });
    const edgeLines = new THREE.LineSegments(edgeGeom, edgeMat);

    state.edgeGeom = edgeGeom;
    state.edges = edgeLines;
    state.scene.add(edgeLines);

    // Fit camera once (gentle)
    if (!state._fitted && nodeCount > 0) {
        state._fitted = true;

        const bbx = new THREE.Box3().setFromObject(nodePoints);
        const size = bbx.getSize(new THREE.Vector3());
        const center = bbx.getCenter(new THREE.Vector3());

        const radius = Math.max(size.x, size.y, size.z) * 0.75 + 1.0;
        state.controls.target.copy(center);

        const dir = new THREE.Vector3().subVectors(state.camera.position, center).normalize();
        if (dir.lengthSq() < 0.001) dir.set(0.5, 0.6, 0.8).normalize();

        state.camera.position.copy(center.clone().add(dir.multiplyScalar(radius * 2.0)));
        state.camera.updateProjectionMatrix();
    }
}

export function disposeNetwork3D(state) {
    if (!state) return;

    cancelAnimationFrame(state._raf);
    window.removeEventListener("resize", state._onResize);

    if (state.nodes) {
        state.scene.remove(state.nodes);
        state.nodeGeom.dispose();
        state.nodes.material.dispose();
    }

    if (state.edges) {
        state.scene.remove(state.edges);
        state.edgeGeom.dispose();
        state.edges.material.dispose();
    }

    state.renderer.dispose();

    if (state.renderer.domElement && state.renderer.domElement.parentElement) {
        state.renderer.domElement.parentElement.removeChild(state.renderer.domElement);
    }
}

function clamp(x, lo, hi) {
    return x < lo ? lo : (x > hi ? hi : x);
}
