import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { BuildGraphTableComponent } from './build-graph-table.component';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { MomentModule } from 'ngx-moment';

describe('BuildGraphTableComponent', () => {
  let component: BuildGraphTableComponent;
  let fixture: ComponentFixture<BuildGraphTableComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        BuildGraphTableComponent,
      ],
      imports: [
        MomentModule,
      ],
      schemas: [
        NO_ERRORS_SCHEMA, // Allow unrecognized elements (other components we don't want to test)
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(BuildGraphTableComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
