/* Copyright (c) 2007 Scott Lembcke
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
 
using System;
namespace CocosPhysics.Chipmunk
{
    public static partial class Physics
    {

//MARK: Contact Set Helpers

// Equal function for arbiterSet.
static bool
arbiterSetEql(cpShape shapes, cpArbiter arb)
{
	cpShape a = shapes[0];
	cpShape b = shapes[1];
	
	return ((a == arb.a && b == arb.b) || (b == arb.a && a == arb.b));
}

//MARK: Collision Handler Set HelperFunctions

// Equals function for collisionHandlers.
static bool
handlerSetEql(cpCollisionHandler *check, cpCollisionHandler *pair)
{
	return ((check.a == pair.a && check.b == pair.b) || (check.b == pair.a && check.a == pair.b));
}

// Transformation function for collisionHandlers.
static object 
handlerSetTrans(cpCollisionHandler *handler, object unused)
{
	cpCollisionHandler *copy = (cpCollisionHandler *)cpcalloc(1, sizeof(cpCollisionHandler));
	(*copy) = (*handler);
	
	return copy;
}

//MARK: Misc Helper Funcs

// Default collision functions.
static bool alwaysCollide(cpArbiter arb, cpSpace space, object data){return 1;}
static void nothing(cpArbiter arb, cpSpace space, object data){}

// function to get the estimated velocity of a shape for the cpBBTree.
static cpVect shapeVelocityFunc(cpShape shape){return shape.body.v;}

static void freeWrap(object ptr, object unused){cpfree(ptr);}

//MARK: Memory Management Functions

cpSpace 
cpSpaceAlloc()
{
	return (cpSpace )cpcalloc(1, sizeof(cpSpace));
}

cpCollisionHandler cpDefaultCollisionHandler = {0, 0, alwaysCollide, alwaysCollide, nothing, nothing, null};

cpSpace*
cpSpaceInit(cpSpace space)
{
#ifndef NDEBUG
	static bool done = false;
	if(!done){
		printf("Initializing cpSpace - Chipmunk v%s (Debug Enabled)\n", cpVersionString);
		printf("Compile with -DNDEBUG defined to disable debug mode and runtime assertion checks\n");
		done = true;
	}
#endif

	space.iterations = 10;
	
	space.gravity = cpvzero;
	space.damping = 1.0f;
	
	space.collisionSlop = 0.1f;
	space.collisionBias = System.Math.Pow(1.0f - 0.1f, 60.0f);
	space.collisionPersistence = 3;
	
	space.locked = 0;
	space.stamp = 0;

	space.staticShapes = cpBBTreeNew((cpSpatialIndexBBFunc)cpShapeGetBB, null);
	space.activeShapes = cpBBTreeNew((cpSpatialIndexBBFunc)cpShapeGetBB, space.staticShapes);
	cpBBTreeSetVelocityFunc(space.activeShapes, (cpBBTreeVelocityFunc)shapeVelocityFunc);
	
	space.allocatedBuffers = cpArrayNew(0);
	
	space.bodies = cpArrayNew(0);
	space.sleepingComponents = cpArrayNew(0);
	space.rousedBodies = cpArrayNew(0);
	
	space.sleepTimeThreshold = double.PositiveInfinity;
	space.idleSpeedThreshold = 0.0f;
	space.enableContactGraph = false;
	
	space.arbiters = cpArrayNew(0);
	space.pooledArbiters = cpArrayNew(0);
	
	space.contactBuffersHead = null;
	space.cachedArbiters = cpHashSetNew(0, (cpHashSetEqlFunc)arbiterSetEql);
	
	space.constraints = cpArrayNew(0);
	
	space.defaultHandler = cpDefaultCollisionHandler;
	space.collisionHandlers = cpHashSetNew(0, (cpHashSetEqlFunc)handlerSetEql);
	cpHashSetSetDefaultValue(space.collisionHandlers, &cpDefaultCollisionHandler);
	
	space.postStepCallbacks = cpArrayNew(0);
	space.skipPostStep = false;
	
	cpBodyInitStatic(&space._staticBody);
	space.staticBody = &space._staticBody;
	
	return space;
}

cpSpace*
cpSpaceNew()
{
	return cpSpaceInit(cpSpaceAlloc());
}

void
cpSpaceDestroy(cpSpace space)
{
	cpSpaceEachBody(space, (cpSpaceBodyIteratorFunc)cpBodyActivate, null);
	
	cpSpatialIndexFree(space.staticShapes);
	cpSpatialIndexFree(space.activeShapes);
	
	cpArrayFree(space.bodies);
	cpArrayFree(space.sleepingComponents);
	cpArrayFree(space.rousedBodies);
	
	cpArrayFree(space.constraints);
	
	cpHashSetFree(space.cachedArbiters);
	
	cpArrayFree(space.arbiters);
	cpArrayFree(space.pooledArbiters);
	
	if(space.allocatedBuffers){
		cpArrayFreeEach(space.allocatedBuffers, cpfree);
		cpArrayFree(space.allocatedBuffers);
	}
	
	if(space.postStepCallbacks){
		cpArrayFreeEach(space.postStepCallbacks, cpfree);
		cpArrayFree(space.postStepCallbacks);
	}
	
	if(space.collisionHandlers) cpHashSetEach(space.collisionHandlers, freeWrap, null);
	cpHashSetFree(space.collisionHandlers);
}

void
cpSpaceFree(cpSpace space)
{
	if(space){
		cpSpaceDestroy(space);
		cpfree(space);
	}
}


//MARK: Collision Handler Function Management

void
cpSpaceAddCollisionHandler(
	cpSpace space,
	cpCollisionType a, cpCollisionType b,
	cpCollisionBeginFunc begin,
	cpCollisionPreSolveFunc preSolve,
	cpCollisionPostSolveFunc postSolve,
	cpCollisionSeparateFunc separate,
	object data
){
	// cpAssertSpaceUnlocked(space);
	
	// Remove any old function so the new one will get added.
	cpSpaceRemoveCollisionHandler(space, a, b);
	
	cpCollisionHandler handler = {
		a, b,
		begin ? begin : alwaysCollide,
		preSolve ? preSolve : alwaysCollide,
		postSolve ? postSolve : nothing,
		separate ? separate : nothing,
		data
	};
	
	cpHashSetInsert(space.collisionHandlers, CP_HASH_PAIR(a, b), &handler, null, (cpHashSetTransFunc)handlerSetTrans);
}

void
cpSpaceRemoveCollisionHandler(cpSpace space, cpCollisionType a, cpCollisionType b)
{
	// cpAssertSpaceUnlocked(space);
	
	struct { cpCollisionType a, b; } ids = {a, b};
	cpCollisionHandler old_handler = (cpCollisionHandler) cpHashSetRemove(space.collisionHandlers, CP_HASH_PAIR(a, b), &ids);
	cpfree(old_handler);
}

void
cpSpaceSetDefaultCollisionHandler(
	cpSpace space,
	cpCollisionBeginFunc begin,
	cpCollisionPreSolveFunc preSolve,
	cpCollisionPostSolveFunc postSolve,
	cpCollisionSeparateFunc separate,
	object data
){
	// cpAssertSpaceUnlocked(space);
	
	cpCollisionHandler handler = new cpCollisionHandler() {
		0, 0,
		begin ? begin : alwaysCollide,
		preSolve ? preSolve : alwaysCollide,
		postSolve ? postSolve : nothing,
		separate ? separate : nothing,
		data
	};
	
	space.defaultHandler = handler;
	cpHashSetSetDefaultValue(space.collisionHandlers, &space.defaultHandler);
}

//MARK: Body, Shape, and Joint Management
cpShape 
cpSpaceAddShape(cpSpace space, cpShape shape)
{
	cpBody body = shape.body;
	if(cpBodyIsStatic(body)) return cpSpaceAddStaticShape(space, shape);
	
	// cpAssertHard(!shape.space, "This shape is already added to a space and cannot be added to another.");
	// cpAssertSpaceUnlocked(space);
	
	cpBodyActivate(body);
	cpBodyAddShape(body, shape);
	
	cpShapeUpdate(shape, body.p, body.rot);
	cpSpatialIndexInsert(space.activeShapes, shape, shape.hashid);
	shape.space = space;
		
	return shape;
}

cpShape 
cpSpaceAddStaticShape(cpSpace space, cpShape shape)
{
	// cpAssertHard(!shape.space, "This shape is already added to a space and cannot be added to another.");
	// cpAssertSpaceUnlocked(space);
	
	cpBody body = shape.body;
	cpBodyAddShape(body, shape);
	cpShapeUpdate(shape, body.p, body.rot);
	cpSpatialIndexInsert(space.staticShapes, shape, shape.hashid);
	shape.space = space;
	
	return shape;
}

cpBody 
cpSpaceAddBody(cpSpace space, cpBody body)
{
	// cpAssertHard(!cpBodyIsStatic(body), "Static bodies cannot be added to a space as they are not meant to be simulated.");
	// cpAssertHard(!body.space, "This body is already added to a space and cannot be added to another.");
	// cpAssertSpaceUnlocked(space);
	
	cpArrayPush(space.bodies, body);
	body.space = space;
	
	return body;
}

cpConstraint 
cpSpaceAddConstraint(cpSpace space, cpConstraint constraint)
{
	// cpAssertHard(!constraint.space, "This shape is already added to a space and cannot be added to another.");
	// cpAssertHard(constraint.a && constraint.b, "Constraint is attached to a null body.");
	// cpAssertSpaceUnlocked(space);
	
	cpBodyActivate(constraint.a);
	cpBodyActivate(constraint.b);
	cpArrayPush(space.constraints, constraint);
	
	// Push onto the heads of the bodies' constraint lists
	cpBody a = constraint.a, *b = constraint.b;
	constraint.next_a = a.constraintList; a.constraintList = constraint;
	constraint.next_b = b.constraintList; b.constraintList = constraint;
	constraint.space = space;
	
	return constraint;
}

struct arbiterFilterContext {
	cpSpace space;
	cpBody body;
	cpShape shape;
};

static bool
cachedArbitersFilter(cpArbiter arb, arbiterFilterContext context)
{
	cpShape shape = context.shape;
	cpBody body = context.body;
	
	
	// Match on the filter shape, or if it's null the filter body
	if(
		(body == arb.body_a && (shape == arb.a || shape == null)) ||
		(body == arb.body_b && (shape == arb.b || shape == null))
	){
		// Call separate when removing shapes.
		if(shape && arb.state != cpArbiterStateCached) cpArbiterCallSeparate(arb, context.space);
		
		cpArbiterUnthread(arb);
		cpArrayDeleteObj(context.space.arbiters, arb);
		cpArrayPush(context.space.pooledArbiters, arb);
		
		return false;
	}
	
	return true;
}

void
cpSpaceFilterArbiters(cpSpace space, cpBody body, cpShape filter)
{
	cpSpaceLock(space); {
		struct arbiterFilterContext context = {space, body, filter};
		cpHashSetFilter(space.cachedArbiters, (cpHashSetFilterFunc)cachedArbitersFilter, &context);
	} cpSpaceUnlock(space, true);
}

void
cpSpaceRemoveShape(cpSpace space, cpShape shape)
{
	cpBody body = shape.body;
	if(cpBodyIsStatic(body)){
		cpSpaceRemoveStaticShape(space, shape);
	} else {
		// cpAssertHard(cpSpaceContainsShape(space, shape), "Cannot remove a shape that was not added to the space. (Removed twice maybe?)");
		// cpAssertSpaceUnlocked(space);
		
		cpBodyActivate(body);
		cpBodyRemoveShape(body, shape);
		cpSpaceFilterArbiters(space, body, shape);
		cpSpatialIndexRemove(space.activeShapes, shape, shape.hashid);
		shape.space = null;
	}
}

void
cpSpaceRemoveStaticShape(cpSpace space, cpShape shape)
{
	// cpAssertHard(cpSpaceContainsShape(space, shape), "Cannot remove a static or sleeping shape that was not added to the space. (Removed twice maybe?)");
	// cpAssertSpaceUnlocked(space);
	
	cpBody body = shape.body;
	if(cpBodyIsStatic(body)) cpBodyActivateStatic(body, shape);
	cpBodyRemoveShape(body, shape);
	cpSpaceFilterArbiters(space, body, shape);
	cpSpatialIndexRemove(space.staticShapes, shape, shape.hashid);
	shape.space = null;
}

void
cpSpaceRemoveBody(cpSpace space, cpBody body)
{
	// cpAssertHard(cpSpaceContainsBody(space, body), "Cannot remove a body that was not added to the space. (Removed twice maybe?)");
	// cpAssertSpaceUnlocked(space);
	
	cpBodyActivate(body);
//	cpSpaceFilterArbiters(space, body, null);
	cpArrayDeleteObj(space.bodies, body);
	body.space = null;
}

void
cpSpaceRemoveConstraint(cpSpace space, cpConstraint constraint)
{
	// cpAssertHard(cpSpaceContainsConstraint(space, constraint), "Cannot remove a constraint that was not added to the space. (Removed twice maybe?)");
	// cpAssertSpaceUnlocked(space);
	
	cpBodyActivate(constraint.a);
	cpBodyActivate(constraint.b);
	cpArrayDeleteObj(space.constraints, constraint);
	
	cpBodyRemoveConstraint(constraint.a, constraint);
	cpBodyRemoveConstraint(constraint.b, constraint);
	constraint.space = null;
}

bool cpSpaceContainsShape(cpSpace space, cpShape shape)
{
	return (shape.space == space);
}

bool cpSpaceContainsBody(cpSpace space, cpBody body)
{
	return (body.space == space);
}

bool cpSpaceContainsConstraint(cpSpace space, cpConstraint constraint)
{
	return (constraint.space == space);
}

//MARK: Static/rogue body conversion.

void
cpSpaceConvertBodyToStatic(cpSpace space, cpBody body)
{
	// cpAssertHard(!cpBodyIsStatic(body), "Body is already static.");
	// cpAssertHard(cpBodyIsRogue(body), "Remove the body from the space before calling this function.");
	// cpAssertSpaceUnlocked(space);
	
	cpBodySetMass(body, double.PositiveInfinity);
	cpBodySetMoment(body, double.PositiveInfinity);
	
	cpBodySetVel(body, cpvzero);
	cpBodySetAngVel(body, 0.0f);
	
	body.node.idleTime = double.PositiveInfinity;
	CP_BODY_FOREACH_SHAPE(body, shape){
		cpSpatialIndexRemove(space.activeShapes, shape, shape.hashid);
		cpSpatialIndexInsert(space.staticShapes, shape, shape.hashid);
	}
}

void
cpSpaceConvertBodyToDynamic(cpSpace space, cpBody body, double m, double i)
{
	// cpAssertHard(cpBodyIsStatic(body), "Body is already dynamic.");
	// cpAssertSpaceUnlocked(space);
	
	cpBodyActivateStatic(body, null);
	
	cpBodySetMass(body, m);
	cpBodySetMoment(body, i);
	
	body.node.idleTime = 0.0f;
	CP_BODY_FOREACH_SHAPE(body, shape){
		cpSpatialIndexRemove(space.staticShapes, shape, shape.hashid);
		cpSpatialIndexInsert(space.activeShapes, shape, shape.hashid);
	}
}

//MARK: Iteration

void
cpSpaceEachBody(cpSpace space, cpSpaceBodyIteratorFunc func, object data)
{
	cpSpaceLock(space); {
		cpArray *bodies = space.bodies;
		
		for(int i=0; i<bodies.num; i++){
			func((cpBody )bodies.arr[i], data);
		}
		
		cpArray *components = space.sleepingComponents;
		for(int i=0; i<components.num; i++){
			cpBody root = (cpBody )components.arr[i];
			
			cpBody body = root;
			while(body){
				cpBody next = body.node.next;
				func(body, data);
				body = next;
			}
		}
	} cpSpaceUnlock(space, true);
}

// typedef struct spaceShapeContext {
	cpSpaceShapeIteratorFunc func;
	object data;
} spaceShapeContext;

static void
spaceEachShapeIterator(cpShape shape, spaceShapeContext *context)
{
	context.func(shape, context.data);
}

void
cpSpaceEachShape(cpSpace space, cpSpaceShapeIteratorFunc func, object data)
{
	cpSpaceLock(space); {
		spaceShapeContext context = {func, data};
		cpSpatialIndexEach(space.activeShapes, (cpSpatialIndexIteratorFunc)spaceEachShapeIterator, &context);
		cpSpatialIndexEach(space.staticShapes, (cpSpatialIndexIteratorFunc)spaceEachShapeIterator, &context);
	} cpSpaceUnlock(space, true);
}

void
cpSpaceEachConstraint(cpSpace space, cpSpaceConstraintIteratorFunc func, object data)
{
	cpSpaceLock(space); {
		cpArray *constraints = space.constraints;
		
		for(int i=0; i<constraints.num; i++){
			func((cpConstraint )constraints.arr[i], data);
		}
	} cpSpaceUnlock(space, true);
}

//MARK: Spatial Index Management

static void
updateBBCache(cpShape shape, object unused)
{
	cpBody body = shape.body;
	cpShapeUpdate(shape, body.p, body.rot);
}

void 
cpSpaceReindexStatic(cpSpace space)
{
	// cpAssertHard(!space.locked, "You cannot manually reindex objects while the space is locked. Wait until the current query or step is complete.");
	
	cpSpatialIndexEach(space.staticShapes, (cpSpatialIndexIteratorFunc)&updateBBCache, null);
	cpSpatialIndexReindex(space.staticShapes);
}

void
cpSpaceReindexShape(cpSpace space, cpShape shape)
{
	// cpAssertHard(!space.locked, "You cannot manually reindex objects while the space is locked. Wait until the current query or step is complete.");
	
	cpBody body = shape.body;
	cpShapeUpdate(shape, body.p, body.rot);
	
	// attempt to rehash the shape in both hashes
	cpSpatialIndexReindexObject(space.activeShapes, shape, shape.hashid);
	cpSpatialIndexReindexObject(space.staticShapes, shape, shape.hashid);
}

void
cpSpaceReindexShapesForBody(cpSpace space, cpBody body)
{
	CP_BODY_FOREACH_SHAPE(body, shape) cpSpaceReindexShape(space, shape);
}


static void
copyShapes(cpShape shape, cpSpatialIndex *index)
{
	cpSpatialIndexInsert(index, shape, shape.hashid);
}

void
cpSpaceUseSpatialHash(cpSpace space, double dim, int count)
{
	cpSpatialIndex *staticShapes = cpSpaceHashNew(dim, count, (cpSpatialIndexBBFunc)cpShapeGetBB, null);
	cpSpatialIndex *activeShapes = cpSpaceHashNew(dim, count, (cpSpatialIndexBBFunc)cpShapeGetBB, staticShapes);
	
	cpSpatialIndexEach(space.staticShapes, (cpSpatialIndexIteratorFunc)copyShapes, staticShapes);
	cpSpatialIndexEach(space.activeShapes, (cpSpatialIndexIteratorFunc)copyShapes, activeShapes);
	
	cpSpatialIndexFree(space.staticShapes);
	cpSpatialIndexFree(space.activeShapes);
	
	space.staticShapes = staticShapes;
	space.activeShapes = activeShapes;
}
}
}