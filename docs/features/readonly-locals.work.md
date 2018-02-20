
locals:

- [x] readonly T x = e;
- [x] let x = e;
- [ ] out let
- [x] disallow unninitialized
- [ ] disallow readonly let
- [ ] disallow readonly var
- [ ] deconstructions
    - [ ] let (x, y) = e;
    - [ ] (let x, let y) = e;
    - [ ] (readonly int x, int y) = e;
- [ ] feature flag
- [ ] ref 
    - [ ] readonly ref  
    - [ ] readonly ref readonly
    - [ ] ref let 
- [ ] foreach
- [ ] for
- [ ] using
- [ ] fixed
- [ ] catch
- [ ] patterns

parameters:

- [x] method
- [ ] constructor
- [ ] operator
- [ ] conversion operator
- [ ] indexer
- [ ] local function
- [ ] anonymous function
- [x] permit readonly this
- [x] permit readonly params
- [ ] lambda expressions
    - [ ] (readonly int x) => {}
    - [ ] (let x) => {}
- [x] disallow in abstract method
- [x] disallow in interface method
- [x] disallow in extern method
- [x] disallow in partial method
- [ ] disallow in delegate
- [x] disallow with out, ref, in
- [ ] insignificant in overrides/overloads
- [ ] feature flag

IDE:

- [ ] keyword completion tests
- [ ] analyzer: suggest to make readonly if not re-assigned

