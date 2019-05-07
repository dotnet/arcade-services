import { TestBed } from '@angular/core/testing';

import { BuildResolverService } from './build-resolver.service';

describe('BuildResolverService', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('should be created', () => {
    const service: BuildResolverService = TestBed.get(BuildResolverService);
    expect(service).toBeTruthy();
  });
});
