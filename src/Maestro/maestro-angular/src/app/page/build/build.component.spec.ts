import { async, ComponentFixture, TestBed } from "@angular/core/testing";
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { RouterTestingModule } from '@angular/router/testing';

import { BuildComponent } from "./build.component";
import { StatefulModule } from 'src/stateful';
import { MaestroService } from 'src/maestro-client';
import { BuildStatusService } from 'src/app/services/build-status.service';
import { MockBuildLink, MockCommitLink } from "src/app/mock-pipes.spec";

describe("BuildComponent", () => {
  let component: BuildComponent;
  let fixture: ComponentFixture<BuildComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        BuildComponent,
        MockBuildLink,
        MockCommitLink,
      ],
      providers: [
        {
          provide: MaestroService,
          useValue: {
          },
        },
        {
          provide: BuildStatusService,
          useValue: {
          },
        }
      ],
      imports: [
        RouterTestingModule,
        StatefulModule,
      ],
      schemas: [
        NO_ERRORS_SCHEMA,
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it("should create", () => {
    expect(component).toBeTruthy();
  });
});
