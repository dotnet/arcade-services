import { TestBed } from '@angular/core/testing';

import { BuildService } from './build.service';
import { MaestroService } from 'src/maestro-client';

describe('BuildService', () => {
  beforeEach(() => {
    const mockMaestroService = jasmine.createSpyObj("MaestroService", ["builds"]);
    TestBed.configureTestingModule({
      providers: [
        { provide: MaestroService, useValue: mockMaestroService },
      ],
    });
  });

  it('should be created', () => {
    const service: BuildService = TestBed.get(BuildService);
    expect(service).toBeTruthy();
  });
});
