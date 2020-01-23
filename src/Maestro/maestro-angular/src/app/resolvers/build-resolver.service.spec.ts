import { TestBed } from '@angular/core/testing';

import { BuildResolverService } from './build-resolver.service';
import { BuildService } from '../services/build.service';

describe('BuildResolverService', () => {
  beforeEach(() => {
    const mockBuildService = jasmine.createSpyObj("BuildService", ["getBuild"]);
    TestBed.configureTestingModule({
      providers: [
        { provide: BuildService, useValue: mockBuildService },
      ],
    });
  });

  it('should be created', () => {
    const service: BuildResolverService = TestBed.get(BuildResolverService);
    expect(service).toBeTruthy();
  });
});
